using DevPulse.Application.Settings;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// M9: co godzinę wybiera użytkowników, którym właśnie wypadł termin digestu (lokalna godzina == <c>send_hour</c>,
/// reguła zmiany czasu z <see cref="NextDigestCalculator"/>), i enqueue'uje <see cref="SendDigestJob"/>.
/// Jeden godzinowy job zamiast joba per użytkownik – zmiana godziny w ustawieniach nie wymaga przepinania jobów.
/// </summary>
public sealed class DigestSchedulerJob(
    IDbContextFactory<DevPulseDbContext> dbContextFactory,
    IBackgroundJobClient backgroundJobClient,
    TimeProvider timeProvider,
    IOptions<DigestPipelineOptions> options,
    ILogger<DigestSchedulerJob> logger)
{
    public const string RecurringJobId = "digest-scheduler";

    /// <summary>Pełna godzina – każdy termin wysyłki to pełna godzina lokalna.</summary>
    public const string Cron = "0 * * * *";

    /// <summary>
    /// Bez ponowień: spóźnione ponowienie trafiłoby poza okno i nic by nie wysłało, a kolejne uruchomienie przyjdzie za godzinę.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Joby pomijają użytkowników z nieważnym tokenem (CLAUDE.md) i bez śledzonych repo – digest nie miałby treści.
        var candidates = await dbContext.UserSettings
            .AsNoTracking()
            .Where(s => s.DigestEnabled && s.EmailConfirmed && s.Email != null)
            .Where(s => dbContext.Users.Any(u => u.Id == s.UserId && !u.TokenInvalid))
            .Where(s => dbContext.TrackedRepositories.Any(r => r.UserId == s.UserId))
            .Select(s => new { s.UserId, s.SendHour, s.TimeZoneId })
            .ToListAsync(cancellationToken);

        var due = new List<(Guid UserId, DateOnly DigestDate)>();
        foreach (var candidate in candidates)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(candidate.TimeZoneId, out var timeZone))
            {
                logger.LogWarning("User {UserId} has unknown time zone {TimeZoneId}; digest skipped", candidate.UserId, candidate.TimeZoneId);
                continue;
            }

            if (NextDigestCalculator.DueLocalDate(now, candidate.SendHour.Value, timeZone, options.Value.SchedulerWindow) is { } digestDate)
            {
                due.Add((candidate.UserId, digestDate));
            }
        }

        await RequeueAbandonedAsync(dbContext, now, cancellationToken);

        if (due.Count == 0)
        {
            logger.LogInformation("Digest scheduler: no digests due");
            return;
        }

        // Jedno zapytanie zamiast N: digesty już utworzone na te dni (SendDigestJob i tak jest idempotentny).
        var userIds = due.Select(d => d.UserId).ToList();
        var dates = due.Select(d => d.DigestDate).Distinct().ToList();
        var existing = (await dbContext.Digests
                .AsNoTracking()
                .Where(d => userIds.Contains(d.UserId) && dates.Contains(d.DigestDate))
                .Select(d => new { d.UserId, d.DigestDate })
                .ToListAsync(cancellationToken))
            .Select(d => (d.UserId, d.DigestDate))
            .ToHashSet();

        var enqueued = 0;
        foreach (var (userId, digestDate) in due.Where(d => !existing.Contains(d)))
        {
            backgroundJobClient.Enqueue<SendDigestJob>(job => job.SendAsync(userId, digestDate, CancellationToken.None));
            enqueued++;
        }

        logger.LogInformation("Digest scheduler: {Enqueued} digests enqueued ({Due} due)", enqueued, due.Count);
    }

    /// <summary>
    /// Porzucone próby (proces padł w trakcie wysyłki, wiersz został <c>Pending</c>) – SendDigestJob przejmie je po
    /// <c>StalePendingAfter</c>. Bez tego taki digest zostałby w historii jako „w trakcie” na zawsze, bo scheduler
    /// pomija dni, dla których wiersz już istnieje.
    /// </summary>
    private async Task RequeueAbandonedAsync(DevPulseDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var staleBefore = now - options.Value.StalePendingAfter;
        var recoverySince = now - options.Value.AbandonedRecoveryWindow;
        var abandoned = await dbContext.Digests
            .AsNoTracking()
            .Where(d => d.Status == DigestStatus.Pending && d.CreatedAt < staleBefore && d.CreatedAt >= recoverySince)
            // Tylko użytkownicy, którym digest nadal się należy – inaczej job i tak kończy się bez przejęcia wiersza.
            .Where(d => dbContext.UserSettings.Any(s => s.UserId == d.UserId && s.DigestEnabled)
                && dbContext.Users.Any(u => u.Id == d.UserId && !u.TokenInvalid))
            .Select(d => new { d.UserId, d.DigestDate })
            .ToListAsync(cancellationToken);

        foreach (var digest in abandoned)
        {
            var (userId, digestDate) = (digest.UserId, digest.DigestDate);
            backgroundJobClient.Enqueue<SendDigestJob>(job => job.SendAsync(userId, digestDate, CancellationToken.None));
        }

        if (abandoned.Count > 0)
        {
            logger.LogWarning("Digest scheduler: {Count} abandoned digests re-enqueued", abandoned.Count);
        }
    }
}
