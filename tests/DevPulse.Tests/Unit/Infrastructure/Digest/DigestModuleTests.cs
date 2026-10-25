using DevPulse.Application.Digests;
using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Ai;
using DevPulse.Infrastructure.DependencyInjection;
using DevPulse.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

[Trait("Category", "Unit")]
public class DigestModuleTests
{
    [Fact]
    public void Should_ResolveDigestBuildingBlocks_When_InfrastructureAdded()
    {
        // Arrange
        using var provider = BuildProvider(new Dictionary<string, string?>());

        // Act
        var composer = provider.GetRequiredService<IDigestComposer>();
        var renderer = provider.GetRequiredService<IDigestEmailRenderer>();
        var prioritizer = provider.GetRequiredService<IAiPrioritizer>();
        var emailSender = provider.GetRequiredService<IEmailSender>();

        // Assert
        composer.Should().BeOfType<DigestComposer>();
        renderer.Should().BeOfType<DigestEmailRenderer>();
        prioritizer.Should().BeOfType<AnthropicPrioritizer>();
        emailSender.Should().BeOfType<SendGridEmailSender>();
    }

    [Fact]
    public void Should_ReadApiKeyFromEnvironmentVariableAndBindSections_When_Configured()
    {
        // Arrange – klucz z ANTHROPIC_API_KEY, nie z sekcji Anthropic
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ANTHROPIC_API_KEY"] = "sk-ant-from-env",
            ["Anthropic:ApiKey"] = "sk-ant-from-appsettings",
            ["Anthropic:MaxTokens"] = "350",
            ["SendGrid:FromEmail"] = "digest@devpulse.example",
            ["SendGrid:SandboxMode"] = "true",
            ["Digest:InactiveAfter"] = "10.00:00:00",
        });

        // Act
        var anthropic = provider.GetRequiredService<IOptions<AnthropicOptions>>().Value;
        var sendGrid = provider.GetRequiredService<IOptions<SendGridOptions>>().Value;
        var rules = provider.GetRequiredService<IOptions<DigestRulesOptions>>().Value;

        // Assert
        anthropic.ApiKey.Should().Be("sk-ant-from-env");
        anthropic.MaxTokens.Should().Be(350);
        anthropic.Model.Should().Be("claude-sonnet-5-5");
        sendGrid.FromEmail.Should().Be("digest@devpulse.example");
        sendGrid.SandboxMode.Should().BeTrue();
        rules.InactiveAfter.Should().Be(TimeSpan.FromDays(10));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] = "Host=localhost;Database=devpulse;Username=devpulse;Password=devpulse";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }
}
