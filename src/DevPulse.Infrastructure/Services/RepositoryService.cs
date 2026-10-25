using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using DevPulse.Infrastructure.GitHub;
using DevPulse.Infrastructure.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DevPulse.Infrastructure.Services;

internal sealed class RepositoryService(
    IDevPulseDbContext dbContext,
    IGitHubClient gitHubClient,
    IMemoryCache cache,
    IBackgroundJobClient backgroundJobClient,
    TimeProvider timeProvider,
    IOptions<RepositoriesOptions> repositoriesOptions,
    IOptions<GitHubOptions> gitHubOptions,
    ILogger<RepositoryService> logger) : IRepositoryService
{
    private const string UniqueViolation = "23505";

    public async Task<AvailableRepositoriesResponse> GetAvailableAsync(
        Guid userId,
        AvailableRepositoriesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var available = request.Refresh
            ? await FetchAvailableAsync(userId, cancellationToken)
            : await GetCachedAvailableAsync(userId, cancellationToken);

        var trackedIds = await dbContext.TrackedRepositories
            .Where(r => r.UserId == userId)
            .Select(r => r.GitHubRepoId)
            .ToListAsync(cancellationToken);
        var tracked = trackedIds.ToHashSet();

        var filtered = available.Repositories
            .Where(r => string.IsNullOrWhiteSpace(request.Search)
                || r.FullName.Contains(request.Search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = filtered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new AvailableRepositoryItem(
                r.Id, r.FullName, r.HtmlUrl, r.IsPrivate, r.Description, r.PushedAt, tracked.Contains(r.Id)))
            .ToList();

        return new AvailableRepositoriesResponse(items, request.Page, request.PageSize, filtered.Count, available.FetchedAt);
    }

    public async Task<IReadOnlyList<TrackedRepositoryResponse>> GetTrackedAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.TrackedRepositories
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.FullName)
            .Select(r => new TrackedRepositoryResponse(r.Id, r.GitHubRepoId, r.FullName, r.HtmlUrl, r.IsPrivate, r.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<TrackedRepositoryResponse> AddAsync(Guid userId, RepositoryAddRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Najpierw tanie sprawdzenia w bazie – przed jakimkolwiek wywołaniem GitHuba.
        var trackedIds = await dbContext.TrackedRepositories
            .Where(r => r.UserId == userId)
            .Select(r => r.GitHubRepoId)
            .ToListAsync(cancellationToken);

        if (trackedIds.Contains(request.GitHubRepoId))
        {
            throw new ConflictException("Repozytorium jest już śledzone.");
        }

        var maxPerUser = repositoriesOptions.Value.MaxPerUser;
        if (trackedIds.Count >= maxPerUser)
        {
            throw new LimitExceededException($"Osiągnięto limit {maxPerUser} śledzonych repozytoriów.");
        }

        // Metadane z GitHuba (cache available albo GET /repositories/{id}) – klient nie może podsunąć nazwy ani cudzego repo.
        var metadata = FindInCache(userId, request.GitHubRepoId)
            ?? await gitHubClient.GetRepositoryAsync(userId, request.GitHubRepoId, cancellationToken)
            ?? throw new NotFoundException("Repozytorium nie istnieje albo nie masz do niego dostępu.");

        var repository = TrackedRepository.Create(
            userId,
            metadata.Id,
            metadata.NodeId,
            new RepoFullName(metadata.Owner, metadata.Name),
            metadata.HtmlUrl,
            metadata.DefaultBranch,
            metadata.IsPrivate,
            timeProvider.GetUtcNow());

        dbContext.TrackedRepositories.Add(repository);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Wyścig dwóch równoległych POST – rozstrzyga unikalny indeks (user_id, github_repo_id).
            throw new ConflictException("Repozytorium jest już śledzone.");
        }

        // Pierwszy snapshot od razu – dashboard pokazuje kartę „Loading”, dopóki job go nie zapisze.
        backgroundJobClient.Enqueue<SnapshotRefreshJob>(job => job.RefreshRepositoryAsync(userId, repository.Id, CancellationToken.None));
        logger.LogInformation(
            "User {UserId} started tracking repository {GitHubRepoId} as {TrackedRepositoryId}",
            userId,
            repository.GitHubRepoId,
            repository.Id);

        return new TrackedRepositoryResponse(
            repository.Id, repository.GitHubRepoId, repository.FullName, repository.HtmlUrl, repository.IsPrivate, repository.CreatedAt);
    }

    public async Task RemoveAsync(Guid userId, Guid trackedRepositoryId, CancellationToken cancellationToken)
    {
        // Filtr po userId: cudze repo wygląda tak samo jak nieistniejące (404). Snapshot usuwa kaskada.
        var deleted = await dbContext.TrackedRepositories
            .Where(r => r.Id == trackedRepositoryId && r.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            throw new NotFoundException();
        }

        logger.LogInformation("User {UserId} stopped tracking repository {TrackedRepositoryId}", userId, trackedRepositoryId);
    }

    private static string CacheKey(Guid userId) => $"github:available:{userId}";

    private async Task<AvailableRepositories> GetCachedAvailableAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.TryGetValue(CacheKey(userId), out AvailableRepositories? cached) && cached is not null
            ? cached
            : await FetchAvailableAsync(userId, cancellationToken);

    private async Task<AvailableRepositories> FetchAvailableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var repositories = await gitHubClient.GetUserRepositoriesAsync(userId, cancellationToken);
        var available = new AvailableRepositories(repositories, timeProvider.GetUtcNow());
        cache.Set(CacheKey(userId), available, gitHubOptions.Value.AvailableCacheDuration);
        return available;
    }

    private GitHubRepository? FindInCache(Guid userId, long gitHubRepoId) =>
        cache.TryGetValue(CacheKey(userId), out AvailableRepositories? cached)
            ? cached?.Repositories.FirstOrDefault(r => r.Id == gitHubRepoId)
            : null;

    private sealed record AvailableRepositories(IReadOnlyList<GitHubRepository> Repositories, DateTimeOffset FetchedAt);
}
