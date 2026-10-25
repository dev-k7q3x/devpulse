using DevPulse.Application.Digests;
using DevPulse.Domain.Enums;

namespace DevPulse.Tests.Builders;

/// <summary>Stan repo jako wejście digestu. Domyślnie: zdrowe repo z aktywnością dzień przed <see cref="TestClock.Now"/>.</summary>
public sealed class RepositoryDigestStateBuilder
{
    private string _repoFullName = "octocat/hello-world";
    private int _openPrCount;
    private int _openIssueCount = 2;
    private CiStatus _ciStatus = CiStatus.Success;
    private DateTimeOffset? _lastActivityAt = TestClock.Now.AddDays(-1);
    private DateTimeOffset? _oldestOpenPrCreatedAt;

    public RepositoryDigestStateBuilder Named(string repoFullName)
    {
        _repoFullName = repoFullName;
        return this;
    }

    public RepositoryDigestStateBuilder WithCi(CiStatus ciStatus)
    {
        _ciStatus = ciStatus;
        return this;
    }

    public RepositoryDigestStateBuilder WithIssues(int openIssueCount)
    {
        _openIssueCount = openIssueCount;
        return this;
    }

    /// <summary>Otwarte PR-y, najstarszy sprzed <paramref name="oldestAgeDays"/> dni względem <see cref="TestClock.Now"/>.</summary>
    public RepositoryDigestStateBuilder WithOpenPullRequests(int count, int oldestAgeDays)
    {
        _openPrCount = count;
        _oldestOpenPrCreatedAt = TestClock.Now.AddDays(-oldestAgeDays);
        return this;
    }

    public RepositoryDigestStateBuilder LastActiveDaysAgo(int days)
    {
        _lastActivityAt = TestClock.Now.AddDays(-days);
        return this;
    }

    public RepositoryDigestStateBuilder WithoutActivityData()
    {
        _lastActivityAt = null;
        return this;
    }

    public RepositoryDigestState Build() =>
        new(_repoFullName, _openPrCount, _openIssueCount, _ciStatus, _lastActivityAt, _oldestOpenPrCreatedAt);
}
