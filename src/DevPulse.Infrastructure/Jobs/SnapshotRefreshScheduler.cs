using DevPulse.Application.Options;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// Rejestruje recurring <see cref="SnapshotRefreshJob"/> przy starcie aplikacji – w module GitHub, nie w Program.cs
/// (plan 04 §3.2: ścieżki równoległe nie edytują wspólnych plików). Aplikacja ma wstać także bez bazy,
/// więc rejestracja jest ponawiana w tle, aż storage Hangfire odpowie.
/// </summary>
internal sealed class SnapshotRefreshScheduler(
    IServiceProvider serviceProvider,
    IOptions<SnapshotOptions> options,
    TimeProvider timeProvider,
    ILogger<SnapshotRefreshScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cron = RecurringSchedule.ToCron(options.Value.RefreshInterval);

        // AddOrUpdate jest synchroniczne – bez tego start hosta czekałby na odpowiedź bazy.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Rozwiązywane dopiero tutaj: utworzenie storage Hangfire łączy się z bazą.
                var recurringJobManager = serviceProvider.GetRequiredService<IRecurringJobManager>();
                recurringJobManager.AddOrUpdate<SnapshotRefreshJob>(
                    SnapshotRefreshJob.RecurringJobId,
                    job => job.RefreshAllAsync(CancellationToken.None),
                    cron);
                logger.LogInformation("Recurring job {JobId} registered with schedule {Cron}", SnapshotRefreshJob.RecurringJobId, cron);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Recurring job {JobId} registration failed; retrying in {Delay}", SnapshotRefreshJob.RecurringJobId, RetryDelay);
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

internal static class RecurringSchedule
{
    /// <summary>
    /// Interwał → wyrażenie cron. Obsługiwane: pełne minuty będące dzielnikiem godziny (np. 15 min → <c>*/15 * * * *</c>)
    /// oraz pełne godziny będące dzielnikiem doby. Inne wartości to błąd konfiguracji.
    /// </summary>
    public static string ToCron(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMinutes(1) || interval.Seconds != 0 || interval.Milliseconds != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interwał musi być pełną liczbą minut (co najmniej 1).");
        }

        var minutes = (int)interval.TotalMinutes;
        if (minutes < 60 && 60 % minutes == 0)
        {
            return minutes == 1 ? "* * * * *" : $"*/{minutes} * * * *";
        }

        if (minutes % 60 == 0 && minutes / 60 <= 24 && 24 % (minutes / 60) == 0)
        {
            var hours = minutes / 60;
            return hours == 24 ? "0 0 * * *" : $"0 */{hours} * * *";
        }

        throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interwał musi dzielić godzinę (minuty) albo dobę (godziny).");
    }
}
