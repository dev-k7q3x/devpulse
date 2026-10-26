using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.DependencyInjection;
using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using FluentAssertions;
using Hangfire.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

[Trait("Category", "Unit")]
public class DigestPipelineModuleTests
{
    [Fact]
    public void Should_RegisterJobsAndScheduler_When_InfrastructureAdded()
    {
        // Arrange – bez rozwiązywania: joby zależą od storage Hangfire, który łączy się z bazą
        var services = BuildServices(new Dictionary<string, string?>());

        // Act
        var registered = services.Select(d => (d.ServiceType, d.ImplementationType)).ToList();

        // Assert
        registered.Should().Contain((typeof(DigestSchedulerJob), typeof(DigestSchedulerJob)));
        registered.Should().Contain((typeof(SendDigestJob), typeof(SendDigestJob)));
        registered.Should().Contain((typeof(IHostedService), typeof(DigestScheduler)));
    }

    [Fact]
    public void Should_HaveSingleEmailSender_When_InfrastructureAdded()
    {
        // Arrange – atrapa LoggingEmailSender z E3C usunięta, został tylko SendGrid
        var services = BuildServices(new Dictionary<string, string?>());

        // Act
        var registrations = services.Where(d => d.ServiceType == typeof(IEmailSender)).ToList();
        using var provider = services.BuildServiceProvider();

        // Assert
        registrations.Should().ContainSingle();
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<SendGridEmailSender>();
    }

    [Fact]
    public void Should_UseDefaultsOrConfiguredValues_When_PipelineOptionsBound()
    {
        // Arrange
        var services = BuildServices(new Dictionary<string, string?> { ["DigestPipeline:StalePendingAfter"] = "00:30:00" });

        // Act
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DigestPipelineOptions>>().Value;

        // Assert
        options.SchedulerWindow.Should().Be(TimeSpan.FromHours(1));
        options.StalePendingAfter.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Should_RoundTripDigestDate_When_HangfireSerializesJobArgument()
    {
        // Arrange – SendDigestJob(userId, DateOnly) trafia do storage Hangfire jako JSON
        var digestDate = new DateOnly(2026, 10, 25);

        // Act
        var json = SerializationHelper.Serialize(digestDate, SerializationOption.User);
        var restored = SerializationHelper.Deserialize<DateOnly>(json, SerializationOption.User);

        // Assert
        restored.Should().Be(digestDate);
    }

    private static ServiceCollection BuildServices(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] = "Host=localhost;Database=devpulse;Username=devpulse;Password=devpulse";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }
}
