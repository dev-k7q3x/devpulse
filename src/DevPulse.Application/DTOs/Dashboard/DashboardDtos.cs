using DevPulse.Domain.Enums;

namespace DevPulse.Application.DTOs.Dashboard;

/// <summary><c>GET /api/dashboard</c> – czytane wyłącznie ze snapshotów w bazie, nigdy z GitHuba.</summary>
public sealed record DashboardResponse(
    DashboardSummary Summary,
    DateTimeOffset? OldestRefreshAt,
    IReadOnlyList<RepositoryCard> Repositories);

public sealed record DashboardSummary(
    int RepositoryCount,
    int TotalOpenPrs,
    int TotalOpenIssues,
    int FailingCiCount);

public sealed record RepositoryCard(
    Guid Id,
    string FullName,
    string HtmlUrl,
    bool IsPrivate,
    RepositoryCardState State,
    int? OpenPrCount,
    int? OpenIssueCount,
    IReadOnlyList<PullRequestItem> OpenPullRequests,
    CiInfo Ci,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? RefreshedAt,
    string? LastError);

public enum RepositoryCardState
{
    /// <summary>Brak snapshotu – job jeszcze go nie zapisał.</summary>
    Loading,
    Fresh,

    /// <summary>Snapshot starszy niż próg z konfiguracji (domyślnie 1 h).</summary>
    Stale,

    /// <summary>Ostatnia próba odświeżenia zakończyła się błędem.</summary>
    Error,
}

public sealed record PullRequestItem(
    int Number,
    string Title,
    string Url,
    string? Author,
    DateTimeOffset CreatedAt,
    bool IsDraft);

/// <param name="Status">Zmapowany status CI.</param>
/// <param name="CommitSha">Commit, którego dotyczy status.</param>
/// <param name="ChecksUrl"><c>{htmlUrl}/commit/{sha}/checks</c>.</param>
public sealed record CiInfo(CiStatus Status, string? CommitSha, string? ChecksUrl);
