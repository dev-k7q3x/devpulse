using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>Readiness Hangfire: storage odpowiada i co najmniej jeden serwer wysłał niedawno heartbeat.</summary>
internal sealed class HangfireHealthCheck(JobStorage storage, TimeProvider timeProvider) : IHealthCheck
{
    /// <summary>Heartbeat serwera Hangfire domyślnie co 30 s – dwie minuty to zapas na chwilowe opóźnienia.</summary>
    public static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(2);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Świadomy wyjątek od reguły async: monitoring API Hangfire nie ma wersji asynchronicznej.
        var servers = storage.GetMonitoringApi().Servers();
        var hasAliveServer = servers.Any(server => server.Heartbeat is { } heartbeat && now - heartbeat <= MaxHeartbeatAge);

        return Task.FromResult(hasAliveServer
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Brak aktywnego serwera Hangfire."));
    }
}
