using DevPulse.Application.Diagnostics;
using DevPulse.Infrastructure.DependencyInjection;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class InfrastructureServiceCollectionExtensionsTests
{
    private const string ConnectionString = "Host=localhost;Database=devpulse;Username=devpulse;Password=devpulse";

    [Fact]
    public void Should_RegisterDatabaseHealthCheckWithReadyTag_When_InfrastructureAdded()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(BuildConfiguration(ConnectionString));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations.Should().ContainSingle(r => r.Name == "postgresql")
            .Which.Tags.Should().Contain(HealthCheckTags.Ready);
    }

    [Fact]
    public void Should_ResolveDbContextWithConfiguredConnectionString_When_InfrastructureAdded()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(ConnectionString));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var dbContext = scope.ServiceProvider.GetRequiredService<DevPulseDbContext>();

        // Assert
        dbContext.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        dbContext.Database.GetConnectionString().Should().Be(ConnectionString);
    }

    [Fact]
    public void Should_NotRegisterAnyHealthCheckWithoutReadyTag_When_InfrastructureAdded()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInfrastructure(BuildConfiguration(ConnectionString));
        using var provider = services.BuildServiceProvider();

        // Assert – /health (liveness) nie może zależeć od żadnego checka zależności
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations.Should().OnlyContain(r => r.Tags.Contains(HealthCheckTags.Ready));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ThrowInvalidOperationException_When_ConnectionStringIsMissing(string? connectionString)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddInfrastructure(BuildConfiguration(connectionString));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Default*");
    }

    private static IConfiguration BuildConfiguration(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();
}
