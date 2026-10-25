using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.GitHub;

/// <summary>
/// GitHub API: REST z ETag dla list repo (odpowiedź 304 nie zużywa limitu) i GraphQL <c>nodes(ids:)</c> dla snapshotów.
/// Budżet limitu i mapowanie błędów HTTP robi <see cref="GitHubRateLimitHandler"/>. Token nigdy nie trafia do logów.
/// </summary>
internal sealed class GitHubClient(
    HttpClient httpClient,
    IGitHubTokenStore tokenStore,
    IMemoryCache cache,
    IOptions<GitHubOptions> options,
    ILogger<GitHubClient> logger) : IGitHubClient
{
    private const string UserRepositoriesPath =
        "user/repos?per_page=100&affiliation=owner,collaborator,organization_member&sort=full_name";

    /// <summary>ETag i sparsowana strona trzymane dłużej niż cache <c>available</c> – kolejne pobranie po 10 min kończy się 304.</summary>
    private static readonly TimeSpan ETagCacheDuration = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions RestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions GraphQlJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepository>();
        string? url = UserRepositoriesPath;

        for (var page = 1; url is not null; page++)
        {
            if (page > options.Value.MaxRepositoryPages)
            {
                logger.LogWarning(
                    "User {UserId} has more than {MaxPages} pages of repositories; the list is truncated",
                    userId,
                    options.Value.MaxRepositoryPages);
                break;
            }

            var result = await GetRepositoryPageAsync(userId, url, cancellationToken);
            repositories.AddRange(result.Repositories);
            url = result.NextUrl;
        }

        return repositories;
    }

    public async Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(
            userId, HttpMethod.Get, $"repositories/{gitHubRepoId.ToString(CultureInfo.InvariantCulture)}", interactive: true, cancellationToken);
        using var response = await SendAsync(request, cancellationToken);

        // 403 bez sygnałów limitu (np. SSO organizacji) traktujemy jak brak dostępu – tak samo jak 404.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        EnsureSuccess(response);
        var repository = await ReadJsonAsync<RestRepository>(response, RestJson, cancellationToken);
        return repository is null ? null : ToModel(repository);
    }

    public async Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
        Guid userId,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        var results = new List<GitHubRepositoryStateResult>(nodeIds.Count);
        foreach (var batch in nodeIds.Chunk(Math.Max(1, options.Value.SnapshotBatchSize)))
        {
            results.AddRange(await GetStatesBatchAsync(userId, batch, cancellationToken));
        }

        return results;
    }

    private async Task<IReadOnlyList<GitHubRepositoryStateResult>> GetStatesBatchAsync(
        Guid userId,
        string[] nodeIds,
        CancellationToken cancellationToken)
    {
        // Snapshoty robią joby – nie podlegają rezerwie budżetu dla żądań interaktywnych.
        using var request = await CreateRequestAsync(userId, HttpMethod.Post, "graphql", interactive: false, cancellationToken);
        request.Content = JsonContent.Create(
            new { query = GitHubQueries.RepositoryStates, variables = new { ids = nodeIds } },
            options: GraphQlJson);

        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response);

        var body = await ReadJsonAsync<GraphQlResponse>(response, GraphQlJson, cancellationToken);
        if (body?.Errors?.Any(e => e.Type == "RATE_LIMITED") == true)
        {
            throw new GitHubRateLimitedException(retryAfter: null);
        }

        var nodes = body?.Data?.Nodes;
        if (nodes is null || nodes.Count != nodeIds.Length)
        {
            throw new GitHubUnavailableException("GitHub zwrócił niekompletną odpowiedź GraphQL.");
        }

        var results = new GitHubRepositoryStateResult[nodeIds.Length];
        for (var i = 0; i < nodeIds.Length; i++)
        {
            results[i] = nodes[i] is { Id: not null } node
                ? new GitHubRepositoryStateResult(nodeIds[i], ToState(nodeIds[i], node), Error: null)
                : new GitHubRepositoryStateResult(nodeIds[i], State: null, Error: ErrorForNode(body!.Errors, i));
        }

        return results;
    }

    private async Task<RepositoryPage> GetRepositoryPageAsync(Guid userId, string url, CancellationToken cancellationToken)
    {
        var cacheKey = (nameof(GitHubClient), userId, url);
        cache.TryGetValue(cacheKey, out CachedPage? cached);

        using var request = await CreateRequestAsync(userId, HttpMethod.Get, url, interactive: true, cancellationToken);
        if (cached is not null)
        {
            request.Headers.IfNoneMatch.Add(cached.ETag);
        }

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            return cached.Page;
        }

        EnsureSuccess(response);
        var repositories = await ReadJsonAsync<List<RestRepository>>(response, RestJson, cancellationToken) ?? [];
        var page = new RepositoryPage(repositories.Select(ToModel).ToList(), ReadNextLink(response));

        if (response.Headers.ETag is { } etag)
        {
            cache.Set(cacheKey, new CachedPage(etag, page), ETagCacheDuration);
        }

        return page;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        Guid userId,
        HttpMethod method,
        string url,
        bool interactive,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenStore.GetAccessTokenAsync(userId, cancellationToken);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Options.Set(GitHubRequestOptions.UserId, userId);
        request.Options.Set(GitHubRequestOptions.Interactive, interactive);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout – nie anulowanie przez wołającego.
            throw new GitHubUnavailableException("GitHub nie odpowiedział w wymaganym czasie.", exception);
        }
    }

    /// <summary>Uszkodzona albo nieoczekiwana odpowiedź to dla wołającego ten sam przypadek co awaria GitHuba.</summary>
    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, JsonSerializerOptions jsonOptions, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(jsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new GitHubUnavailableException("GitHub zwrócił nieprawidłową odpowiedź.", exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubUnavailableException($"GitHub zwrócił nieoczekiwany status {(int)response.StatusCode}.");
        }
    }

    private static string? ReadNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        // <https://api.github.com/user/repos?page=2>; rel="next", <…>; rel="last"
        foreach (var part in string.Join(',', values).Split(','))
        {
            var segments = part.Split(';', StringSplitOptions.TrimEntries);
            if (segments.Length >= 2 && segments[1] == "rel=\"next\"" && segments[0].StartsWith('<') && segments[0].EndsWith('>'))
            {
                return segments[0][1..^1];
            }
        }

        return null;
    }

    private static string ErrorForNode(IReadOnlyList<GraphQlError>? errors, int index)
    {
        var error = errors?.FirstOrDefault(e => e.Path is { Count: >= 2 } path
            && path[0].ValueKind == JsonValueKind.String && path[0].GetString() == "nodes"
            && path[1].ValueKind == JsonValueKind.Number && path[1].GetInt32() == index);

        return error?.Type == "FORBIDDEN" ? "Forbidden" : "NotFound";
    }

    private static GitHubRepository ToModel(RestRepository repository) => new(
        repository.Id,
        repository.NodeId,
        repository.Owner.Login,
        repository.Name,
        repository.FullName,
        repository.HtmlUrl,
        string.IsNullOrEmpty(repository.DefaultBranch) ? "main" : repository.DefaultBranch,
        repository.Private,
        repository.Description,
        repository.PushedAt);

    private static GitHubRepositoryState ToState(string nodeId, GraphQlRepository node)
    {
        var commit = node.DefaultBranchRef?.Target;
        var pullRequests = node.PullRequests?.Nodes?
            .OfType<GraphQlPullRequest>()
            .Select(pr => new GitHubPullRequest(pr.Number, pr.Title, pr.Url, pr.Author?.Login, pr.CreatedAt, pr.IsDraft))
            .ToList() ?? [];

        return new GitHubRepositoryState(
            nodeId,
            node.Owner?.Login ?? string.Empty,
            node.Name ?? string.Empty,
            node.Url ?? string.Empty,
            // Puste repo nie ma default brancha – job zostawia wtedy dotychczasową nazwę.
            node.DefaultBranchRef?.Name ?? string.Empty,
            node.IsPrivate,
            node.PullRequests?.TotalCount ?? 0,
            node.Issues?.TotalCount ?? 0,
            pullRequests,
            GitHubCiStatusMapper.FromRollupState(commit?.StatusCheckRollup?.State),
            commit?.Oid,
            node.PushedAt);
    }

    private sealed record RepositoryPage(IReadOnlyList<GitHubRepository> Repositories, string? NextUrl);

    private sealed record CachedPage(EntityTagHeaderValue ETag, RepositoryPage Page);
}
