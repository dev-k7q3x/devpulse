using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;

namespace DevPulse.Tests.Builders;

public sealed class RepositorySnapshotBuilder
{
    private Guid _trackedRepositoryId = Guid.CreateVersion7();
    private int _openPrCount = 2;
    private int _openIssueCount = 5;
    private List<OpenPullRequest> _openPullRequests =
    [
        new(42, "Add dashboard", "https://github.com/octocat/hello-world/pull/42", "octocat", TestClock.Now.AddDays(-1), false),
        new(41, "WIP: digest", "https://github.com/octocat/hello-world/pull/41", null, TestClock.Now.AddDays(-9), true),
    ];
    private CiStatus _ciStatus = CiStatus.Success;
    private string? _ciCommitSha = new('a', RepositorySnapshot.CommitShaLength);
    private DateTimeOffset? _lastActivityAt = TestClock.Now.AddHours(-3);
    private string? _lastError;
    private DateTimeOffset _now = TestClock.Now;

    public RepositorySnapshotBuilder ForRepository(Guid trackedRepositoryId)
    {
        _trackedRepositoryId = trackedRepositoryId;
        return this;
    }

    public RepositorySnapshotBuilder WithCounts(int openPrCount, int openIssueCount)
    {
        _openPrCount = openPrCount;
        _openIssueCount = openIssueCount;
        return this;
    }

    public RepositorySnapshotBuilder WithOpenPullRequests(IEnumerable<OpenPullRequest> openPullRequests)
    {
        _openPullRequests = openPullRequests.ToList();
        return this;
    }

    public RepositorySnapshotBuilder WithCi(CiStatus ciStatus, string? commitSha = null)
    {
        _ciStatus = ciStatus;
        _ciCommitSha = commitSha;
        return this;
    }

    public RepositorySnapshotBuilder WithLastError(string lastError)
    {
        _lastError = lastError;
        return this;
    }

    public RepositorySnapshotBuilder At(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    public RepositorySnapshot Build()
    {
        var snapshot = RepositorySnapshot.Create(
            _trackedRepositoryId,
            _openPrCount,
            _openIssueCount,
            _openPullRequests,
            _ciStatus,
            _ciCommitSha,
            _lastActivityAt,
            _now);

        if (_lastError is not null)
        {
            snapshot.RecordFailure(_lastError, _now);
        }

        return snapshot;
    }
}
