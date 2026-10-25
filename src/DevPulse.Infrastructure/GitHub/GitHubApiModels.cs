using System.Text.Json;
using DevPulse.Domain.Enums;

namespace DevPulse.Infrastructure.GitHub;

/// <summary>Element odpowiedzi REST <c>GET /user/repos</c> i <c>GET /repositories/{id}</c> (snake_case).</summary>
internal sealed record RestRepository(
    long Id,
    string NodeId,
    RestOwner Owner,
    string Name,
    string FullName,
    string HtmlUrl,
    string? DefaultBranch,
    bool Private,
    string? Description,
    DateTimeOffset? PushedAt);

internal sealed record RestOwner(string Login);

internal sealed record GraphQlResponse(GraphQlData? Data, IReadOnlyList<GraphQlError>? Errors);

internal sealed record GraphQlData(IReadOnlyList<GraphQlRepository?>? Nodes);

/// <summary>Błąd GraphQL – <c>path</c> wskazuje element <c>nodes</c> (np. <c>["nodes", 3]</c>).</summary>
internal sealed record GraphQlError(string? Type, IReadOnlyList<JsonElement>? Path);

internal sealed record GraphQlRepository(
    string? Id,
    string? Name,
    GraphQlOwner? Owner,
    string? Url,
    bool IsPrivate,
    DateTimeOffset? PushedAt,
    GraphQlBranchRef? DefaultBranchRef,
    GraphQlPullRequestConnection? PullRequests,
    GraphQlCount? Issues);

internal sealed record GraphQlOwner(string Login);

internal sealed record GraphQlBranchRef(string Name, GraphQlCommit? Target);

internal sealed record GraphQlCommit(string? Oid, GraphQlStatusCheckRollup? StatusCheckRollup);

internal sealed record GraphQlStatusCheckRollup(string? State);

internal sealed record GraphQlPullRequestConnection(int TotalCount, IReadOnlyList<GraphQlPullRequest?>? Nodes);

internal sealed record GraphQlPullRequest(int Number, string Title, string Url, bool IsDraft, DateTimeOffset CreatedAt, GraphQlOwner? Author);

internal sealed record GraphQlCount(int TotalCount);

internal static class GitHubCiStatusMapper
{
    /// <summary>
    /// <c>statusCheckRollup.state</c> ostatniego commita na default branch. <c>ERROR</c> (np. awaria runnera) liczymy jako
    /// porażkę, <c>EXPECTED</c> (wymagany check jeszcze nie zgłoszony) jako oczekujący; brak rollupu = brak CI.
    /// </summary>
    public static CiStatus FromRollupState(string? state) => state switch
    {
        "SUCCESS" => CiStatus.Success,
        "FAILURE" or "ERROR" => CiStatus.Failure,
        "PENDING" or "EXPECTED" => CiStatus.Pending,
        _ => CiStatus.None,
    };
}

internal static class GitHubQueries
{
    /// <summary>
    /// Stan wielu repo jednym zapytaniem. <c>issues.totalCount</c> nie liczy PR-ów (w przeciwieństwie do REST
    /// <c>open_issues_count</c>). PR-y: łączna liczba + 10 najnowszych do snapshotu.
    /// </summary>
    public const string RepositoryStates = """
        query RepositoryStates($ids: [ID!]!) {
          nodes(ids: $ids) {
            ... on Repository {
              id
              name
              owner { login }
              url
              isPrivate
              pushedAt
              defaultBranchRef {
                name
                target {
                  ... on Commit {
                    oid
                    statusCheckRollup { state }
                  }
                }
              }
              pullRequests(states: OPEN, first: 10, orderBy: { field: CREATED_AT, direction: DESC }) {
                totalCount
                nodes { number title url isDraft createdAt author { login } }
              }
              issues(states: OPEN) { totalCount }
            }
          }
        }
        """;
}
