using System.Net;
using System.Text.Json;
using DevPulse.Application.Digests;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Ai;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

[Trait("Category", "Unit")]
public class AnthropicPrioritizerTests
{
    private const string ApiKey = "sk-ant-test-key";

    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);

    private readonly IReadOnlyList<RepositoryDigestState> _repositories =
    [
        new RepositoryDigestStateBuilder().Named("acme/api").WithCi(CiStatus.Failure).WithOpenPullRequests(4, oldestAgeDays: 3).Build(),
        new RepositoryDigestStateBuilder().Named("acme/web").WithOpenPullRequests(1, oldestAgeDays: 12).LastActiveDaysAgo(2).Build(),
    ];

    [Fact]
    public async Task Should_ReturnValidatedPrioritiesAndTokenUsage_When_ResponseValid()
    {
        // Arrange
        var handler = Respond("message-priorities.json");

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Priorities.Select(p => (p.RepoFullName, p.Severity)).Should().Equal(
            ("acme/api", PrioritySeverity.High),
            ("acme/web", PrioritySeverity.Medium));
        result.Priorities[0].Text.Should().Be("Napraw nieprzechodzące CI na main – blokuje 4 otwarte PR-y.");
        result.Usage.Model.Should().Be("claude-sonnet-5-5");
        result.Usage.InputTokens.Should().Be(312);
        result.Usage.OutputTokens.Should().Be(87);
    }

    [Fact]
    public async Task Should_SendAuthenticatedStructuredOutputRequest_When_Prioritizing()
    {
        // Arrange
        var handler = Respond("message-priorities.json");

        // Act
        await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert – nagłówki
        var request = handler.LastRequest!;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/messages"));
        request.Headers.GetValues("x-api-key").Should().Equal(ApiKey);
        request.Headers.GetValues("anthropic-version").Should().Equal("2026-10-21");

        // Assert – body: model, limit tokenów, structured output, bez sampling params
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("claude-sonnet-5-5");
        root.GetProperty("max_tokens").GetInt32().Should().Be(400);
        root.GetProperty("thinking").GetProperty("type").GetString().Should().Be("between_tools");
        root.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("low");
        root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString().Should().Be("json_schema");
        root.TryGetProperty("temperature", out _).Should().BeFalse();
        root.GetProperty("system").GetString().Should().Contain("maksymalnie 5");
    }

    [Fact]
    public async Task Should_SendCompactInputWithAgesInsteadOfTimestamps_When_Prioritizing()
    {
        // Arrange
        var handler = Respond("message-priorities.json");

        // Act
        await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var input = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        using var inputJson = JsonDocument.Parse(input);
        inputJson.RootElement.GetProperty("today").GetString().Should().Be("2026-10-21");
        var web = inputJson.RootElement.GetProperty("repositories")[1];
        web.GetProperty("repo").GetString().Should().Be("acme/web");
        web.GetProperty("ci").GetString().Should().Be("success");
        web.GetProperty("oldestOpenPrAgeDays").GetInt32().Should().Be(12);
        web.GetProperty("daysSinceActivity").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData("message-max-tokens.json")]
    [InlineData("message-refusal.json")]
    [InlineData("message-garbage-text.json")]
    [InlineData("message-unknown-repositories.json")]
    public async Task Should_ReturnNullForRuleFallback_When_ResponseUnusable(string fixture)
    {
        // Arrange – ucięty JSON, odmowa, tekst zamiast JSON, wyłącznie nieprawidłowe pozycje
        var handler = Respond(fixture);

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Should_ReturnEmptyPrioritiesWithUsage_When_ModelFindsNothingToDo()
    {
        // Arrange – pusta lista to poprawna odpowiedź, nie błąd
        var handler = Respond("message-empty-priorities.json");

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Priorities.Should().BeEmpty();
        result.Usage.OutputTokens.Should().Be(9);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Should_ReturnNull_When_ApiReturnsError(HttpStatusCode status)
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(status, StubHttpMessageHandler.Fixture("Anthropic", "error-overloaded.json"));

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Should_ReturnNull_When_NetworkFails()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Should_ReturnNull_When_ApiExceedsTimeout()
    {
        // Arrange – odpowiedź nie przychodzi, czas płynie przez FakeTimeProvider
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var prioritizing = CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Act
        _timeProvider.Advance(TimeSpan.FromSeconds(21));
        var result = await prioritizing.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Should_PropagateCancellation_When_CallerCancels()
    {
        // Arrange – anulowanie przez wołającego (np. zamknięcie aplikacji) to nie fallback
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource();
        var prioritizing = CreatePrioritizer(handler).PrioritizeAsync(_repositories, cancellation.Token);

        // Act
        await cancellation.CancelAsync();
        var act = () => prioritizing;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Should_SkipApiCall_When_ApiKeyMissing()
    {
        // Arrange
        var handler = Respond("message-priorities.json");

        // Act
        var result = await CreatePrioritizer(handler, apiKey: null).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_SkipApiCall_When_NoRepositories()
    {
        // Arrange – brak repo = brak kosztu AI
        var handler = Respond("message-priorities.json");

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync([], CancellationToken.None);

        // Assert
        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_KeepAtMostFivePriorities_When_ModelReturnsMore()
    {
        // Arrange
        var repositories = Enumerable.Range(1, 7)
            .Select(i => new RepositoryDigestStateBuilder().Named($"acme/repo-{i}").WithCi(CiStatus.Failure).Build())
            .ToList();
        var handler = RespondWithPriorities(Enumerable.Range(1, 7).Select(i => ($"acme/repo-{i}", $"Zadanie {i}.")));

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(repositories, CancellationToken.None);

        // Assert
        result!.Priorities.Should().HaveCount(5);
        result.Priorities[^1].Text.Should().Be("Zadanie 5.");
    }

    [Fact]
    public async Task Should_KeepFirstPriorityPerRepository_When_ModelRepeatsRepository()
    {
        // Arrange – jak w fallbacku: jedno repo = najwyżej jeden priorytet (pierwszy = najważniejszy według modelu)
        var handler = RespondWithPriorities(
        [
            ("acme/api", "Napraw CI."),
            ("ACME/API", "Przejrzyj PR-y."),
            ("acme/web", "Zamknij stary PR."),
        ]);

        // Act
        var result = await CreatePrioritizer(handler).PrioritizeAsync(_repositories, CancellationToken.None);

        // Assert
        result!.Priorities.Select(p => (p.RepoFullName, p.Text)).Should().Equal(
            ("acme/api", "Napraw CI."),
            ("acme/web", "Zamknij stary PR."));
    }

    private static StubHttpMessageHandler RespondWithPriorities(IEnumerable<(string Repo, string Text)> items)
    {
        var output = JsonSerializer.Serialize(new { priorities = items.Select(i => new { repo = i.Repo, text = i.Text, severity = "Low" }) });
        var message = JsonSerializer.Serialize(new
        {
            model = "claude-sonnet-5-5",
            content = new[] { new { type = "text", text = output } },
            stop_reason = "end_turn",
            usage = new { input_tokens = 300, output_tokens = 120 },
        });
        return StubHttpMessageHandler.Returning(HttpStatusCode.OK, message);
    }

    private static StubHttpMessageHandler Respond(string fixture) =>
        StubHttpMessageHandler.Returning(HttpStatusCode.OK, StubHttpMessageHandler.Fixture("Anthropic", fixture));

    private AnthropicPrioritizer CreatePrioritizer(StubHttpMessageHandler handler, string? apiKey = ApiKey) => new(
        new HttpClient(handler),
        Options.Create(new AnthropicOptions { ApiKey = apiKey }),
        _timeProvider,
        NullLogger<AnthropicPrioritizer>.Instance);
}
