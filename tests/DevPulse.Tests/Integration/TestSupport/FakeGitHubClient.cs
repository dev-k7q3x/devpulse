using System.Collections.Concurrent;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Domain.Enums;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>
/// IGitHubClient w pamięci, konfigurowany per użytkownik – testy działające równolegle na jednej fabryce
/// nie widzą nawzajem swoich danych. Nieskonfigurowany użytkownik nie ma żadnych repo.
/// </summary>
public sealed class FakeGitHubClient : IGitHubClient
{
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<GitHubRepository>> _repositories = new();
    private readonly ConcurrentDictionary<Guid, Exception> _failures = new();
    private readonly ConcurrentDictionary<Guid, int> _listCalls = new();

    public static GitHubRepository Repository(long id, string owner, string name, bool isPrivate = false) => new(
        id,
        $"R_node{id}",
        owner,
        name,
        $"{owner}/{name}",
        $"https://github.com/{owner}/{name}",
        "main",
        isPrivate,
        Description: $"Repo {name}",
        PushedAt: new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    public void SetRepositories(Guid userId, params GitHubRepository[] repositories) => _repositories[userId] = repositories;

    /// <summary>Każde wywołanie dla użytkownika rzuca <paramref name="exception"/> (np. token odwołany, limit).</summary>
    public void FailWith(Guid userId, Exception exception) => _failures[userId] = exception;

    public int ListCalls(Guid userId) => _listCalls.GetValueOrDefault(userId);

    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken)
    {
        ThrowIfFailing(userId);
        _listCalls.AddOrUpdate(userId, 1, (_, count) => count + 1);
        return Task.FromResult(_repositories.GetValueOrDefault(userId) ?? []);
    }

    public Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken)
    {
        ThrowIfFailing(userId);
        return Task.FromResult(_repositories.GetValueOrDefault(userId)?.FirstOrDefault(r => r.Id == gitHubRepoId));
    }

    public Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
        Guid userId,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken)
    {
        ThrowIfFailing(userId);
        IReadOnlyList<GitHubRepositoryStateResult> results = nodeIds
            .Select(nodeId => new GitHubRepositoryStateResult(nodeId, State: null, Error: "NotFound"))
            .ToList();
        return Task.FromResult(results);
    }

    private void ThrowIfFailing(Guid userId)
    {
        if (_failures.TryGetValue(userId, out var exception))
        {
            throw exception;
        }
    }
}

/// <summary>IGitHubClient dla testów joba: stan repo albo wyjątek ustawiany wprost w teście.</summary>
public sealed class StateGitHubClient : IGitHubClient
{
    public Func<IReadOnlyList<string>, IReadOnlyList<GitHubRepositoryStateResult>> States { get; set; } =
        nodeIds => nodeIds.Select(id => new GitHubRepositoryStateResult(id, null, "NotFound")).ToList();

    public List<IReadOnlyList<string>> RequestedNodeIds { get; } = [];

    public static GitHubRepositoryState State(
        string nodeId,
        string owner = "octocat",
        string name = "hello-world",
        int openPrCount = 3,
        int openIssueCount = 4,
        CiStatus ciStatus = CiStatus.Failure) => new(
        nodeId,
        owner,
        name,
        $"https://github.com/{owner}/{name}",
        "main",
        IsPrivate: false,
        openPrCount,
        openIssueCount,
        [new GitHubPullRequest(7, "Fix login", $"https://github.com/{owner}/{name}/pull/7", "monalisa", new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero), false)],
        ciStatus,
        new string('b', 40),
        new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Job snapshotu nie pobiera listy repo.");

    public Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Job snapshotu nie pobiera pojedynczego repo.");

    public Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
        Guid userId,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken)
    {
        RequestedNodeIds.Add(nodeIds);
        return Task.FromResult(States(nodeIds));
    }
}
