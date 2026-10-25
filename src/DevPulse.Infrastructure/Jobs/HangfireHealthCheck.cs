using Hangfire;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>Readiness Hangfire: storage odpowiada i co najmniej jeden serwer wysłał niedawno heartbeat.</summary>
internal sealed class HangfireHealthCheck(JobStorage storage, TimeProvider timeProvider) : IHealthCheck
{
    /// <summary>Heartbeat serwera Hangfire domyślnie co 30 s – dwie minuty to zapas na chwilowe opóźnienia.</summary>
    public static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Hangfire.PostgreSql przy niedostępnej bazie ponawia połączenie synchronicznie (ok. 90 s) i ignoruje token anulowania,
    /// więc timeout rejestracji health checka nie działa – readiness ma odpowiedzieć szybko.
    /// </summary>
    public static readonly TimeSpan StorageTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IList<ServerDto> servers;
        try
        {
            // Świadomy wyjątek od reguły async: monitoring API Hangfire nie ma wersji asynchronicznej. Wywołanie idzie
            // na wątek puli i czekamy najwyżej StorageTimeout; zawieszone wywołanie dokończy się w tle samo.
            servers = await Task.Run(() => storage.GetMonitoringApi().Servers(), CancellationToken.None)
                .WaitAsync(StorageTimeout, timeProvider, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Storage Hangfire nie odpowiada.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Storage Hangfire zgłosił błąd.", exception);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hasAliveServer = servers.Any(server => server.Heartbeat is { } heartbeat && now - heartbeat <= MaxHeartbeatAge);

        return hasAliveServer
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Brak aktywnego serwera Hangfire.");
    }
}
