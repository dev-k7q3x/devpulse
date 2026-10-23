using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;

namespace DevPulse.Tests.Builders;

public sealed class TrackedRepositoryBuilder
{
    private static long _nextGitHubRepoId = 5_000_000;

    private Guid _userId = Guid.CreateVersion7();
    private long _gitHubRepoId = Interlocked.Increment(ref _nextGitHubRepoId);
    private RepoFullName _fullName = new("octocat", "hello-world");
    private string _defaultBranch = "main";
    private bool _isPrivate;
    private DateTimeOffset _now = TestClock.Now;

    public TrackedRepositoryBuilder ForUser(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public TrackedRepositoryBuilder WithGitHubRepoId(long gitHubRepoId)
    {
        _gitHubRepoId = gitHubRepoId;
        return this;
    }

    public TrackedRepositoryBuilder WithFullName(string owner, string name)
    {
        _fullName = new RepoFullName(owner, name);
        return this;
    }

    public TrackedRepositoryBuilder AsPrivate()
    {
        _isPrivate = true;
        return this;
    }

    public TrackedRepositoryBuilder At(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    public TrackedRepository Build() =>
        TrackedRepository.Create(
            _userId,
            _gitHubRepoId,
            $"R_kgDO{_gitHubRepoId}",
            _fullName,
            $"https://github.com/{_fullName.Value}",
            _defaultBranch,
            _isPrivate,
            _now);
}
