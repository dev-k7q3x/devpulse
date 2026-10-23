using DevPulse.Domain.Enums;

namespace DevPulse.Application.Models.GitHub;

/// <summary>Repozytorium z listy <c>GET /user/repos</c> albo <c>GET /repositories/{id}</c>.</summary>
public sealed record GitHubRepository(
    long Id,
    string NodeId,
    string Owner,
    string Name,
    string FullName,
    string HtmlUrl,
    string DefaultBranch,
    bool IsPrivate,
    string? Description,
    DateTimeOffset? PushedAt);

/// <summary>Stan repo z zapytania GraphQL <c>nodes(ids:)</c> – wejście do snapshotu.</summary>
public sealed record GitHubRepositoryState(
    string NodeId,
    string Owner,
    string Name,
    string HtmlUrl,
    string DefaultBranch,
    bool IsPrivate,
    int OpenPrCount,
    int OpenIssueCount,
    IReadOnlyList<GitHubPullRequest> OpenPullRequests,
    CiStatus CiStatus,
    string? CiCommitSha,
    DateTimeOffset? PushedAt);

public sealed record GitHubPullRequest(
    int Number,
    string Title,
    string Url,
    string? Author,
    DateTimeOffset CreatedAt,
    bool IsDraft);

/// <summary>Wynik pobrania stanu jednego repo: stan albo bezpieczny kod błędu (np. <c>NotFound</c>, <c>Forbidden</c>).</summary>
public sealed record GitHubRepositoryStateResult(string NodeId, GitHubRepositoryState? State, string? Error);
