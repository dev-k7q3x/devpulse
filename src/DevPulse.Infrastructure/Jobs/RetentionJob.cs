using DevPulse.Application.DigestHistory;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// Codziennie usuwa digesty starsze niż okno historii (<c>created_at &lt; now − RetentionPeriod</c>) dla wszystkich
/// użytkowników jednym DELETE (indeks <c>ix_digests_created_at</c>). Bez HttpContext – kontekst z fabryki.
/// </summary>
public sealed class RetentionJob(
    IDbContextFactory<DevPulseDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IOptions<DigestHistoryOptions> options,
    ILogger<RetentionJob> logger)
{
    public const string RecurringJobId = "digest-retention";

    /// <summary>03:00 UTC – poza godzinami wysyłki większości digestów.</summary>
    public const string Cron = "0 3 * * *";

    /// <returns>Liczba usuniętych digestów.</returns>
    [AutomaticRetry(Attempts = 3)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = DigestHistoryRules.RetentionCutoff(timeProvider.GetUtcNow(), options.Value.RetentionPeriod);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await dbContext.Digests
            .Where(d => d.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Digest retention removed {DeletedCount} digests created before {Cutoff}", deleted, cutoff);
        return deleted;
    }
}
