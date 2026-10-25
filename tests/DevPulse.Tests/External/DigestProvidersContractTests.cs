using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Ai;
using DevPulse.Infrastructure.Email;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.External;

/// <summary>
/// Prawdziwe wywołania Anthropic i SendGrid (sandbox mode – mail nie wychodzi). NIGDY w CI, tylko na wyraźne polecenie:
/// <c>DEVPULSE_RUN_EXTERNAL=1 dotnet test ./tests/DevPulse.Tests --filter Category=External</c>
/// (bez zmiennej testy są pomijane – <see cref="ExternalFactAttribute"/>).
/// Klucze z user-secrets projektu Api albo env: <c>ANTHROPIC_API_KEY</c>, <c>SendGrid:ApiKey</c>, <c>SendGrid:FromEmail</c>, <c>SendGrid:TestRecipient</c>.
/// </summary>
[Trait("Category", "External")]
public class DigestProvidersContractTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables()
        .Build();

    [ExternalFact]
    public async Task Should_ReturnPrioritiesWithTokenUsage_When_CallingRealAnthropicApi()
    {
        // Arrange
        var apiKey = _configuration[AnthropicOptions.ApiKeyVariable];
        apiKey.Should().NotBeNullOrWhiteSpace("test kontraktowy wymaga ANTHROPIC_API_KEY");
        using var httpClient = new HttpClient();
        var prioritizer = new AnthropicPrioritizer(
            httpClient,
            Options.Create(new AnthropicOptions { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(60) }),
            TimeProvider.System,
            NullLogger<AnthropicPrioritizer>.Instance);
        var repositories = new[]
        {
            new RepositoryDigestStateBuilder().Named("acme/api").WithCi(Domain.Enums.CiStatus.Failure).WithOpenPullRequests(4, 3).Build(),
            new RepositoryDigestStateBuilder().Named("acme/web").WithOpenPullRequests(1, 15).Build(),
        };

        // Act
        var result = await prioritizer.PrioritizeAsync(repositories, CancellationToken.None);

        // Assert
        result.Should().NotBeNull("odpowiedź musi przejść walidację structured output");
        result!.Priorities.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(5);
        result.Usage.InputTokens.Should().BePositive();
        result.Usage.OutputTokens.Should().BeInRange(1, 400);
    }

    [ExternalFact]
    public async Task Should_AcceptEmailInSandboxMode_When_CallingRealSendGridApi()
    {
        // Arrange
        var options = new SendGridOptions
        {
            ApiKey = _configuration["SendGrid:ApiKey"],
            FromEmail = _configuration["SendGrid:FromEmail"] ?? string.Empty,
            SandboxMode = true,
        };
        var recipient = _configuration["SendGrid:TestRecipient"];
        recipient.Should().NotBeNullOrWhiteSpace("test kontraktowy wymaga SendGrid:TestRecipient");
        using var httpClient = new HttpClient();
        var sender = new SendGridEmailSender(httpClient, Options.Create(options), NullLogger<SendGridEmailSender>.Instance);

        // Act
        var act = () => sender.SendAsync(
            new EmailMessage(recipient!, "DevPulse – test kontraktowy", "<p>Test sandbox</p>", "Test sandbox"),
            CancellationToken.None);

        // Assert – sandbox: 200 bez wysyłki; błąd konfiguracji (klucz, nadawca) rzuci EmailDeliveryException
        await act.Should().NotThrowAsync();
    }
}
