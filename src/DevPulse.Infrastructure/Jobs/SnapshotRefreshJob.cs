using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using DevPulse.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// M4: odświeża <c>repository_snapshots</c> z GitHub GraphQL. Recurring <see cref="RefreshAllAsync"/> co
/// <c>Snapshot:RefreshInterval</c> rozdziela pracę na joby per użytkownik; po <c>POST /api/repositories</c> leci
/// <see cref="RefreshRepositoryAsync"/>. Bez HttpContext – userId zawsze jawnie.
/// Bez ponowień Hangfire: kolejne odświeżenie i tak przyjdzie za 15 min, a błąd zapisujemy w snapshocie.
/// </summary>
public sealed class SnapshotRefreshJob(
    IDbContextFactory<DevPulseDbContext> dbContextFactory,
    IGitHubClient gitHubClient,
    IBackgroundJobClient backgroundJobClient,
    TimeProvider timeProvider,
    IOptions<SnapshotOptions> snapshotOptions,
    ILogger<SnapshotRefreshJob> logger)
{
    public const string RecurringJobId = "snapshot-refresh";

    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    /// <summary>Aktywni użytkownicy (logowanie w oknie albo włączony digest) z ważnym tokenem i co najmniej jednym repo.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var activeSince = timeProvider.GetUtcNow() - snapshotOptions.Value.ActiveUserWindow;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userIds = await dbContext.Users
            .AsNoTracking()
            .Where(u => !u.TokenInvalid)
            .Where(u => u.LastLoginAt > activeSince
                || dbContext.UserSettings.Any(s => s.UserId == u.Id && s.DigestEnabled))
            .Where(u => dbContext.TrackedRepositories.Any(r => r.UserId == u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            backgroundJobClient.Enqueue<SnapshotRefreshJob>(job => job.RefreshUserAsync(userId, CancellationToken.None));
        }

        logger.LogInformation("Snapshot refresh scheduled for {UserCount} active users", userIds.Count);
    }

    [AutomaticRetry(Attempts = 0)]
    public Task RefreshUserAsync(Guid userId, CancellationToken cancellationToken) =>
        RefreshAsync(userId, trackedRepositoryId: null, cancellationToken);

    /// <summary>Pierwszy snapshot po dodaniu repo. Repo usunięte w międzyczasie – job kończy się bez błędu.</summary>
    [AutomaticRetry(Attempts = 0)]
    public Task RefreshRepositoryAsync(Guid userId, Guid trackedRepositoryId, CancellationToken cancellationToken) =>
        RefreshAsync(userId, trackedRepositoryId, cancellationToken);

    private async Task RefreshAsync(Guid userId, Guid? trackedRepositoryId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var repositories = await dbContext.TrackedRepositories
            .Where(r => r.UserId == userId && (trackedRepositoryId == null || r.Id == trackedRepositoryId))
            .ToListAsync(cancellationToken);
        if (repositories.Count == 0)
        {
            return;
        }

        var repositoryIds = repositories.Select(r => r.Id).ToList();
        var snapshots = await dbContext.RepositorySnapshots
            .Where(s => repositoryIds.Contains(s.TrackedRepositoryId))
            .ToDictionaryAsync(s => s.TrackedRepositoryId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        IReadOnlyList<GitHubRepositoryStateResult> results;
        try
        {
            results = await gitHubClient.GetRepositoryStatesAsync(userId, repositories.Select(r => r.GitHubNodeId).ToList(), cancellationToken);
        }
        catch (GitHubTokenInvalidException)
        {
            // Klient już oznaczył token jako nieważny – kolejne odświeżenia pominą użytkownika aż do ponownego logowania.
            RecordFailureForAll(snapshots.Values, "TokenInvalid", now);
            await SaveIgnoringRacesAsync(dbContext, userId, cancellationToken);
            logger.LogWarning("Snapshot refresh skipped for user {UserId}: GitHub token is invalid", userId);
            return;
        }
        catch (GitHubRateLimitedException)
        {
            RecordFailureForAll(snapshots.Values, "RateLimited", now);
            await SaveIgnoringRacesAsync(dbContext, userId, cancellationToken);
            logger.LogWarning("Snapshot refresh for user {UserId} stopped by GitHub rate limit", userId);
            return;
        }
        catch (GitHubUnavailableException exception)
        {
            RecordFailureForAll(snapshots.Values, "Unavailable", now);
            await SaveIgnoringRacesAsync(dbContext, userId, cancellationToken);
            logger.LogWarning(exception, "Snapshot refresh for user {UserId} failed: GitHub unavailable", userId);
            return;
        }

        var resultsByNodeId = results.ToDictionary(r => r.NodeId, StringComparer.Ordinal);
        foreach (var repository in repositories)
        {
            snapshots.TryGetValue(repository.Id, out var snapshot);
            if (!resultsByNodeId.TryGetValue(repository.GitHubNodeId, out var result) || result.State is null)
            {
                RecordFailure(repository, snapshot, result?.Error ?? "NotFound", now);
                continue;
            }

            Apply(dbContext, repository, snapshot, result.State, now);
        }

        await SaveIgnoringRacesAsync(dbContext, userId, cancellationToken);
    }

    /// <summary>
    /// Dwa wyścigi bez szkody dla danych: równoległy job wstawił już pierwszy snapshot (23505) albo repo usunięto
    /// w trakcie odświeżania (23503). Kolejne odświeżenie i tak zapisze aktualny stan.
    /// </summary>
    private async Task SaveIgnoringRacesAsync(DevPulseDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation or ForeignKeyViolation })
        {
            logger.LogInformation("Snapshot refresh for user {UserId} skipped a concurrent change", userId);
        }
    }

    private void Apply(
        DevPulseDbContext dbContext,
        TrackedRepository repository,
        RepositorySnapshot? snapshot,
        GitHubRepositoryState state,
        DateTimeOffset now)
    {
        UpdateMetadataIfChanged(repository, state, now);

        var pullRequests = state.OpenPullRequests
            .Select(pr => new OpenPullRequest(pr.Number, pr.Title, pr.Url, pr.Author, pr.CreatedAt.ToUniversalTime(), pr.IsDraft))
            .ToList();
        var commitSha = state.CiCommitSha is { Length: RepositorySnapshot.CommitShaLength } sha ? sha : null;
        var lastActivityAt = state.PushedAt?.ToUniversalTime();

        if (snapshot is null)
        {
            dbContext.RepositorySnapshots.Add(RepositorySnapshot.Create(
                repository.Id, state.OpenPrCount, state.OpenIssueCount, pullRequests, state.CiStatus, commitSha, lastActivityAt, now));
        }
        else
        {
            snapshot.RecordSuccess(state.OpenPrCount, state.OpenIssueCount, pullRequests, state.CiStatus, commitSha, lastActivityAt, now);
        }
    }

    /// <summary>Repo mogło zmienić nazwę, właściciela, widoczność albo default branch – identyfikujemy je po node id.</summary>
    private void UpdateMetadataIfChanged(TrackedRepository repository, GitHubRepositoryState state, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(state.Owner) || string.IsNullOrWhiteSpace(state.Name) || string.IsNullOrWhiteSpace(state.HtmlUrl))
        {
            return;
        }

        var defaultBranch = string.IsNullOrWhiteSpace(state.DefaultBranch) ? repository.DefaultBranch : state.DefaultBranch;
        var fullName = new RepoFullName(state.Owner, state.Name);
        if (fullName.Value == repository.FullName
            && state.HtmlUrl == repository.HtmlUrl
            && defaultBranch == repository.DefaultBranch
            && state.IsPrivate == repository.IsPrivate)
        {
            return;
        }

        repository.UpdateMetadata(fullName, state.HtmlUrl, defaultBranch, state.IsPrivate, now);
        logger.LogInformation("Repository {TrackedRepositoryId} metadata updated from GitHub", repository.Id);
    }

    /// <summary>Stare dane zostają – zmienia się tylko czas próby i kod błędu. Bez snapshotu karta zostaje w stanie „Loading”.</summary>
    private void RecordFailure(TrackedRepository repository, RepositorySnapshot? snapshot, string error, DateTimeOffset now)
    {
        snapshot?.RecordFailure(error, now);
        logger.LogWarning("Repository {TrackedRepositoryId} could not be refreshed: {Error}", repository.Id, error);
    }

    private static void RecordFailureForAll(IEnumerable<RepositorySnapshot> snapshots, string error, DateTimeOffset now)
    {
        foreach (var snapshot in snapshots)
        {
            snapshot.RecordFailure(error, now);
        }
    }
}
