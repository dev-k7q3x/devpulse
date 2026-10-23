using DevPulse.Domain.Enums;

namespace DevPulse.Domain.Entities;

/// <summary>
/// Cache stanu repozytorium (1:1 z <see cref="TrackedRepository"/>, nadpisywany). Świeżość liczona z <see cref="RefreshedAt"/>,
/// próg w konfiguracji. Dashboard czyta wyłącznie tę tabelę – nigdy GitHuba.
/// </summary>
public sealed class RepositorySnapshot
{
    public const int MaxOpenPullRequests = 10;
    public const int CommitShaLength = 40;
    public const int LastErrorMaxLength = 500;

    private RepositorySnapshot()
    {
    }

    public Guid TrackedRepositoryId { get; private set; }

    /// <summary><c>pullRequests(states: OPEN).totalCount</c>.</summary>
    public int OpenPrCount { get; private set; }

    /// <summary><c>issues(states: OPEN).totalCount</c> – bez PR-ów (w przeciwieństwie do REST <c>open_issues_count</c>).</summary>
    public int OpenIssueCount { get; private set; }

    public List<OpenPullRequest> OpenPullRequests { get; private set; } = [];

    public CiStatus CiStatus { get; private set; }

    public string? CiCommitSha { get; private set; }

    /// <summary><c>pushedAt</c> repo; <c>null</c> dla pustego repo.</summary>
    public DateTimeOffset? LastActivityAt { get; private set; }

    /// <summary>Czas ostatniego udanego pobrania danych.</summary>
    public DateTimeOffset RefreshedAt { get; private set; }

    /// <summary>Czas ostatniej próby, udanej lub nie.</summary>
    public DateTimeOffset LastAttemptAt { get; private set; }

    /// <summary>Krótki, bezpieczny kod błędu ostatniej próby (np. <c>NotFound</c>). Bez treści odpowiedzi i tokenów.</summary>
    public string? LastError { get; private set; }

    public static RepositorySnapshot Create(
        Guid trackedRepositoryId,
        int openPrCount,
        int openIssueCount,
        IEnumerable<OpenPullRequest> openPullRequests,
        CiStatus ciStatus,
        string? ciCommitSha,
        DateTimeOffset? lastActivityAt,
        DateTimeOffset now)
    {
        if (trackedRepositoryId == Guid.Empty)
        {
            throw new ArgumentException("Brak id repozytorium.", nameof(trackedRepositoryId));
        }

        var snapshot = new RepositorySnapshot { TrackedRepositoryId = trackedRepositoryId };
        snapshot.RecordSuccess(openPrCount, openIssueCount, openPullRequests, ciStatus, ciCommitSha, lastActivityAt, now);
        return snapshot;
    }

    /// <param name="openPrCount">Liczba wszystkich otwartych PR.</param>
    /// <param name="openIssueCount">Liczba otwartych issue (bez PR).</param>
    /// <param name="openPullRequests">Najnowsze otwarte PR – zapisujemy pierwsze <see cref="MaxOpenPullRequests"/>.</param>
    /// <param name="ciStatus">Zmapowany status CI.</param>
    /// <param name="ciCommitSha">Commit, którego dotyczy status.</param>
    /// <param name="lastActivityAt"><c>pushedAt</c> repo.</param>
    /// <param name="now">Bieżący czas UTC z TimeProvider.</param>
    public void RecordSuccess(
        int openPrCount,
        int openIssueCount,
        IEnumerable<OpenPullRequest> openPullRequests,
        CiStatus ciStatus,
        string? ciCommitSha,
        DateTimeOffset? lastActivityAt,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openPrCount);
        ArgumentOutOfRangeException.ThrowIfNegative(openIssueCount);
        ArgumentNullException.ThrowIfNull(openPullRequests);
        Guard.Utc(now, nameof(now));

        if (ciCommitSha is not null && ciCommitSha.Length != CommitShaLength)
        {
            throw new ArgumentException($"SHA commita musi mieć {CommitShaLength} znaków.", nameof(ciCommitSha));
        }

        OpenPrCount = openPrCount;
        OpenIssueCount = openIssueCount;
        OpenPullRequests = openPullRequests.Take(MaxOpenPullRequests).ToList();
        CiStatus = ciStatus;
        CiCommitSha = ciCommitSha;
        LastActivityAt = lastActivityAt is { } activity ? Guard.Utc(activity, nameof(lastActivityAt)) : null;
        RefreshedAt = now;
        LastAttemptAt = now;
        LastError = null;
    }

    /// <summary>Nieudana próba: stare dane zostają (lepsze niż puste), zmienia się tylko czas próby i błąd.</summary>
    public void RecordFailure(string error, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        LastAttemptAt = Guard.Utc(now, nameof(now));
        LastError = Guard.Truncate(error, LastErrorMaxLength);
    }
}
