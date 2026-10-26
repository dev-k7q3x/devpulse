using System.Collections.Concurrent;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>
/// IGitHubClient, który rzuca przy każdym wywołaniu i zapisuje, dla kogo go wołano – dowód, że dashboard nie woła GitHuba.
/// Wywołania zapisywane per użytkownik, bo w tej samej fabryce mogą działać joby innych testów.
/// </summary>
public sealed class ThrowingGitHubClient : IGitHubClient
{
    private readonly ConcurrentBag<Guid> _calledFor = [];

    public bool WasCalledFor(Guid userId) => _calledFor.Contains(userId);

    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken) =>
        throw Called(userId);

    public Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken) =>
        throw Called(userId);

    public Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
        Guid userId,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken) =>
        throw Called(userId);

    private InvalidOperationException Called(Guid userId)
    {
        _calledFor.Add(userId);
        return new InvalidOperationException("Ten test nie pozwala wołać GitHuba.");
    }
}
