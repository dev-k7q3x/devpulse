using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// Rejestruje recurring <see cref="DigestSchedulerJob"/> przy starcie – w module digestu, nie w Program.cs
/// (wzorzec <see cref="SnapshotRefreshScheduler"/> i <see cref="RetentionScheduler"/>). Aplikacja ma wstać także bez bazy,
/// więc rejestracja jest ponawiana w tle, aż storage Hangfire odpowie.
/// </summary>
internal sealed class DigestScheduler(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<DigestScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // AddOrUpdate jest synchroniczne – bez tego start hosta czekałby na odpowiedź bazy.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Rozwiązywane dopiero tutaj: utworzenie storage Hangfire łączy się z bazą.
                var recurringJobManager = serviceProvider.GetRequiredService<IRecurringJobManager>();
                recurringJobManager.AddOrUpdate<DigestSchedulerJob>(
                    DigestSchedulerJob.RecurringJobId,
                    job => job.RunAsync(CancellationToken.None),
                    DigestSchedulerJob.Cron);
                logger.LogInformation("Recurring job {JobId} registered with schedule {Cron}", DigestSchedulerJob.RecurringJobId, DigestSchedulerJob.Cron);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Recurring job {JobId} registration failed; retrying in {Delay}", DigestSchedulerJob.RecurringJobId, RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
