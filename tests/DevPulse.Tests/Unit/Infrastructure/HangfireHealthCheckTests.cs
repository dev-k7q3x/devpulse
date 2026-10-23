using DevPulse.Infrastructure.Jobs;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class HangfireHealthCheckTests
{
    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);

    [Fact]
    public async Task Should_ReturnHealthy_When_ServerSentRecentHeartbeat()
    {
        // Arrange
        var check = CreateCheck(new ServerDto { Name = "api-1", Heartbeat = TestClock.Now.UtcDateTime.AddSeconds(-30) });

        // Act
        var result = await check.CheckHealthAsync(CreateContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Should_ReturnUnhealthy_When_LastHeartbeatTooOld()
    {
        // Arrange
        var staleHeartbeat = TestClock.Now.UtcDateTime - HangfireHealthCheck.MaxHeartbeatAge - TimeSpan.FromSeconds(1);
        var check = CreateCheck(new ServerDto { Name = "api-1", Heartbeat = staleHeartbeat });

        // Act
        var result = await check.CheckHealthAsync(CreateContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Should_ReturnUnhealthy_When_NoServerRegistered()
    {
        // Arrange
        var check = CreateCheck();

        // Act
        var result = await check.CheckHealthAsync(CreateContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    private HangfireHealthCheck CreateCheck(params ServerDto[] servers)
    {
        var monitoringApi = new Mock<IMonitoringApi>();
        monitoringApi.Setup(api => api.Servers()).Returns(servers.ToList());

        var storage = new Mock<JobStorage>();
        storage.Setup(s => s.GetMonitoringApi()).Returns(monitoringApi.Object);

        return new HangfireHealthCheck(storage.Object, _timeProvider);
    }

    private static HealthCheckContext CreateContext() => new()
    {
        Registration = new HealthCheckRegistration("hangfire", Mock.Of<IHealthCheck>(), HealthStatus.Unhealthy, ["ready"]),
    };
}
