using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.DependencyInjection;
using DevPulse.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class DigestHistoryModuleTests
{
    [Fact]
    public void Should_RegisterServiceJobAndScheduler_When_InfrastructureAdded()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Act
        services.AddInfrastructure(Configuration(new() { ["Digests:RetentionPeriod"] = "14.00:00:00" }));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Assert
        scope.ServiceProvider.GetRequiredService<IDigestService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<RetentionJob>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().ContainSingle(s => s is RetentionScheduler);
        provider.GetRequiredService<IOptions<DigestHistoryOptions>>().Value.RetentionPeriod.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void Should_DefaultToThirtyDays_When_SectionMissing()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(Configuration([]));
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IOptions<DigestHistoryOptions>>().Value.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:Default"] = "Host=localhost;Database=devpulse;Username=devpulse;Password=devpulse";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
