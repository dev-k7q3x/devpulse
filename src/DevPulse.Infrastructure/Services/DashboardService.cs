using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DevPulse.Application.DTOs.Dashboard;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Services;

/// <summary>Karty dashboardu wyłącznie z bazy (tracked_repositories LEFT JOIN repository_snapshots) – bez wywołań GitHuba.</summary>
internal sealed class DashboardService(
    IDevPulseDbContext dbContext,
    IOptions<SnapshotOptions> snapshotOptions,
    TimeProvider timeProvider) : IDashboardService
{
    public async Task<DashboardResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
                from repository in dbContext.TrackedRepositories.AsNoTracking()
                where repository.UserId == userId
                join snapshot in dbContext.RepositorySnapshots.AsNoTracking()
                    on repository.Id equals snapshot.TrackedRepositoryId into snapshots
                from snapshot in snapshots.DefaultIfEmpty()
                orderby repository.FullName
                select new { repository.Id, repository.FullName, repository.HtmlUrl, repository.IsPrivate, Snapshot = (RepositorySnapshot?)snapshot })
            .ToListAsync(cancellationToken);

        var staleBefore = StaleBefore();
        var cards = rows
            .Select(row => ToCard(row.Id, row.FullName, row.HtmlUrl, row.IsPrivate, row.Snapshot, staleBefore))
            .ToList();
        var snapshotsTaken = rows.Select(row => row.Snapshot).OfType<RepositorySnapshot>().ToList();

        var summary = new DashboardSummary(
            RepositoryCount: cards.Count,
            TotalOpenPrs: snapshotsTaken.Sum(s => s.OpenPrCount),
            TotalOpenIssues: snapshotsTaken.Sum(s => s.OpenIssueCount),
            FailingCiCount: snapshotsTaken.Count(s => s.CiStatus == CiStatus.Failure));

        DateTimeOffset? oldestRefreshAt = snapshotsTaken.Count == 0 ? null : snapshotsTaken.Min(s => s.RefreshedAt);

        return new DashboardResponse(summary, oldestRefreshAt, cards);
    }

    public async Task<string> GetVersionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
                from repository in dbContext.TrackedRepositories.AsNoTracking()
                where repository.UserId == userId
                join snapshot in dbContext.RepositorySnapshots.AsNoTracking()
                    on repository.Id equals snapshot.TrackedRepositoryId into snapshots
                from snapshot in snapshots.DefaultIfEmpty()
                select new
                {
                    repository.Id,
                    repository.UpdatedAt,
                    RefreshedAt = snapshot == null ? (DateTimeOffset?)null : snapshot.RefreshedAt,
                    LastAttemptAt = snapshot == null ? (DateTimeOffset?)null : snapshot.LastAttemptAt,
                })
            .ToListAsync(cancellationToken);

        var staleBefore = StaleBefore();

        // Liczba repo i ich id (dodanie/usunięcie), zmiany metadanych, max(refreshed_at, last_attempt_at) z kontraktu
        // oraz liczba kart po progu świeżości – Fresh → Stale zmienia odpowiedź bez zmiany danych w bazie.
        var version = new StringBuilder()
            .Append(userId.ToString("N")).Append('|')
            .Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(string.Join(',', rows.Select(r => r.Id.ToString("N")).Order(StringComparer.Ordinal))).Append('|')
            .Append(Ticks(rows.Select(r => (DateTimeOffset?)r.UpdatedAt).Max())).Append('|')
            .Append(Ticks(rows.Select(r => r.RefreshedAt).Max())).Append('|')
            .Append(Ticks(rows.Select(r => r.LastAttemptAt).Max())).Append('|')
            .Append(rows.Count(r => r.RefreshedAt is { } refreshedAt && refreshedAt < staleBefore).ToString(CultureInfo.InvariantCulture))
            .ToString();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(version));
        return $"\"{Convert.ToHexStringLower(hash.AsSpan(0, 16))}\"";
    }

    private DateTimeOffset StaleBefore() => timeProvider.GetUtcNow() - snapshotOptions.Value.StaleAfter;

    private static string Ticks(DateTimeOffset? value) =>
        value?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static RepositoryCard ToCard(
        Guid id,
        string fullName,
        string htmlUrl,
        bool isPrivate,
        RepositorySnapshot? snapshot,
        DateTimeOffset staleBefore)
    {
        if (snapshot is null)
        {
            return new RepositoryCard(
                id, fullName, htmlUrl, isPrivate, RepositoryCardState.Loading,
                OpenPrCount: null, OpenIssueCount: null, OpenPullRequests: [],
                new CiInfo(CiStatus.None, CommitSha: null, ChecksUrl: null),
                LastActivityAt: null, RefreshedAt: null, LastError: null);
        }

        var state = snapshot.LastError is not null
            ? RepositoryCardState.Error
            : snapshot.RefreshedAt < staleBefore ? RepositoryCardState.Stale : RepositoryCardState.Fresh;

        var checksUrl = snapshot.CiCommitSha is { } sha ? $"{htmlUrl}/commit/{sha}/checks" : null;

        return new RepositoryCard(
            id, fullName, htmlUrl, isPrivate, state,
            snapshot.OpenPrCount,
            snapshot.OpenIssueCount,
            snapshot.OpenPullRequests
                .Select(pr => new PullRequestItem(pr.Number, pr.Title, pr.Url, pr.Author, pr.CreatedAt, pr.IsDraft))
                .ToList(),
            new CiInfo(snapshot.CiStatus, snapshot.CiCommitSha, checksUrl),
            snapshot.LastActivityAt,
            snapshot.RefreshedAt,
            snapshot.LastError);
    }
}
