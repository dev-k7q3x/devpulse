using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// Rejestruje recurring <see cref="RetentionJob"/> przy starcie – w module historii digestów, nie w Program.cs
/// (ten sam wzorzec co <see cref="SnapshotRefreshScheduler"/>). Aplikacja ma wstać także bez bazy,
/// więc rejestracja jest ponawiana w tle, aż storage Hangfire odpowie.
/// </summary>
internal sealed class RetentionScheduler(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<RetentionScheduler> logger) : BackgroundService
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
                recurringJobManager.AddOrUpdate<RetentionJob>(
                    RetentionJob.RecurringJobId,
                    job => job.RunAsync(CancellationToken.None),
                    RetentionJob.Cron);
                logger.LogInformation("Recurring job {JobId} registered with schedule {Cron}", RetentionJob.RecurringJobId, RetentionJob.Cron);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Recurring job {JobId} registration failed; retrying in {Delay}", RetentionJob.RecurringJobId, RetryDelay);
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
