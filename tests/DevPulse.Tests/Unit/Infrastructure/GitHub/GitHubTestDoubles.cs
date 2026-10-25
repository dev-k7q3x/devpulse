using System.Net;
using System.Text;
using DevPulse.Application.Errors;
using DevPulse.Infrastructure.GitHub;

namespace DevPulse.Tests.Unit.Infrastructure.GitHub;

/// <summary>Fake HttpMessageHandler: odpowiedzi skryptowane w teście, zapis żądań (bez sieci).</summary>
internal sealed class ScriptedGitHubHandler(Func<RecordedRequest, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>Opóźnienie odpowiedzi – honoruje token anulowania (timeout HttpClient).</summary>
    public TimeSpan Delay { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.IfNoneMatch.ToString(),
            body);
        _requests.Add(recorded);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return respond(recorded, _requests.Count);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string IfNoneMatch, string? Body);

/// <summary>Token store w pamięci – bez bazy i Data Protection.</summary>
internal sealed class FakeGitHubTokenStore : IGitHubTokenStore
{
    public const string AccessToken = "gho_unitTestToken";

    public List<Guid> InvalidatedUsers { get; } = [];

    public bool TokenInvalid { get; set; }

    public Task<string> GetAccessTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        TokenInvalid ? throw new GitHubTokenInvalidException() : Task.FromResult(AccessToken);

    public Task MarkTokenInvalidAsync(Guid userId, CancellationToken cancellationToken)
    {
        InvalidatedUsers.Add(userId);
        return Task.CompletedTask;
    }
}

internal static class GitHubResponses
{
    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GitHub", name));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage FromFixture(string name, HttpStatusCode status = HttpStatusCode.OK) =>
        Json(Fixture(name), status);

    public static HttpResponseMessage WithRateLimit(this HttpResponseMessage response, int remaining, DateTimeOffset resetAt, string resource = "core")
    {
        response.Headers.Add("x-ratelimit-remaining", remaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
        response.Headers.Add("x-ratelimit-reset", resetAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        response.Headers.Add("x-ratelimit-resource", resource);
        return response;
    }
}
