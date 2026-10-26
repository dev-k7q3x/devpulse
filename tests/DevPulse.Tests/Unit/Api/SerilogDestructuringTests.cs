using DevPulse.Api.Logging;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DevPulse.Tests.Unit.Api;

/// <summary>Dane wrażliwe nie trafiają do logów nawet przy destrukturyzacji całego obiektu (<c>{@...}</c>).</summary>
[Trait("Category", "Unit")]
public class SerilogDestructuringTests
{
    [Fact]
    public void Should_LogUserWithoutEncryptedToken_When_UserDestructured()
    {
        // Arrange
        var user = new UserBuilder().WithLogin("octocat").Build();

        // Act
        var rendered = Log(logger => logger.Information("Zalogowano {@User}", user));

        // Assert
        rendered.Should().Contain("octocat").And.Contain(user.Id.ToString());
        rendered.Should().NotContain(user.AccessTokenEncrypted).And.NotContain("AccessTokenEncrypted");
    }

    [Fact]
    public void Should_LogGitHubLoginWithoutAccessTokenOrEmail_When_Destructured()
    {
        // Arrange
        var login = new GitHubLogin(42, "octocat", null, "octo@example.com", "gho_secret_token", "repo");

        // Act
        var rendered = Log(logger => logger.Information("Callback {@Login}", login));

        // Assert
        rendered.Should().Contain("octocat");
        rendered.Should().NotContain("gho_secret_token").And.NotContain("octo@example.com");
    }

    [Fact]
    public void Should_LogOnlySubject_When_EmailMessageDestructured()
    {
        // Arrange – treść maila potwierdzającego zawiera link z tokenem
        var message = new EmailMessage(
            "octo@example.com",
            "Potwierdź adres",
            "<a href=\"https://devpulse.example/confirm-email?token=secret-token\">Potwierdź</a>",
            "https://devpulse.example/confirm-email?token=secret-token");

        // Act
        var rendered = Log(logger => logger.Information("Wysyłka {@Message}", message));

        // Assert
        rendered.Should().Contain("Potwierdź adres");
        rendered.Should().NotContain("secret-token").And.NotContain("octo@example.com");
    }

    [Fact]
    public void Should_LogSettingsWithoutEmailAndTokenHash_When_Destructured()
    {
        // Arrange
        var settings = new UserSettingsBuilder()
            .WithEmail("octo@example.com")
            .WithPendingEmail("new@example.com", new byte[32], TestClock.Now.AddDays(1))
            .Build();

        // Act
        var rendered = Log(logger => logger.Information("Ustawienia {@Settings}", settings));

        // Assert
        rendered.Should().Contain("HasPendingEmail");
        rendered.Should().NotContain("octo@example.com").And.NotContain("new@example.com").And.NotContain("TokenHash");
    }

    private static string Log(Action<ILogger> write)
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration()
            .ApplyDevPulseDestructuring()
            .WriteTo.Sink(sink)
            .CreateLogger();

        write(logger);

        return string.Join(Environment.NewLine, sink.Events.Select(e =>
            e.RenderMessage() + " " + string.Join(" ", e.Properties.Select(p => $"{p.Key}={p.Value}"))));
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
