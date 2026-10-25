using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class EmailConfirmationMessageTests
{
    private const string Link = "https://devpulse.example/confirm-email?token=abc_DEF-123";

    [Fact]
    public void Should_ContainLinkAndAddress_When_MessageCreated()
    {
        // Act
        var message = EmailConfirmationMessage.Create("new@example.com", Link);

        // Assert
        message.To.Should().Be("new@example.com");
        message.Subject.Should().Be(EmailConfirmationMessage.Subject);
        message.HtmlBody.Should().Contain($"href=\"{Link}\"").And.Contain("new@example.com").And.Contain("24 godziny");
        message.PlainTextBody.Should().Contain(Link);
    }

    [Fact]
    public void Should_HtmlEncodeAddress_When_AddressContainsMarkup()
    {
        // Arrange – adres przechodzi walidację emaila, ale HTML i tak nie może go interpretować
        const string address = "\"<script>\"@example.com";

        // Act
        var message = EmailConfirmationMessage.Create(address, Link);

        // Assert
        message.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("https://devpulse.example")]
    [InlineData("https://devpulse.example/")]
    public void Should_BuildSpaLinkWithEscapedToken_When_BaseUrlConfigured(string baseUrl)
    {
        // Act
        var link = SendEmailConfirmationJob.BuildConfirmationLink(baseUrl, "abc_DEF-123");

        // Assert – link do strony SPA, która dopiero wysyła POST
        link.Should().Be("https://devpulse.example/confirm-email?token=abc_DEF-123");
    }

    [Fact]
    public void Should_Throw_When_PublicBaseUrlMissing()
    {
        // Act
        var act = () => SendEmailConfirmationJob.BuildConfirmationLink(" ", "token");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*App__PublicBaseUrl*");
    }

    [Theory]
    [InlineData("jan.kowalski@example.com", "j***@example.com")]
    [InlineData("bez-malpy", "***")]
    public void Should_MaskRecipient_When_FakeSenderLogs(string address, string expected)
    {
        // Act
        var masked = LoggingEmailSender.MaskRecipient(address);

        // Assert
        masked.Should().Be(expected);
    }

    [Fact]
    public async Task Should_NotLogBody_When_FakeSenderUsed()
    {
        // Arrange – treść maila zawiera link z tokenem
        var logger = new CapturingLogger();
        var sender = new LoggingEmailSender(logger, Environment(Environments.Development));

        // Act
        var messageId = await sender.SendAsync(EmailConfirmationMessage.Create("new@example.com", Link), CancellationToken.None);

        // Assert
        messageId.Should().BeNull();
        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain(EmailConfirmationMessage.Subject).And.Contain("n***@example.com").And.NotContain("abc_DEF-123");
    }

    [Fact]
    public async Task Should_Throw_When_FakeSenderUsedInProduction()
    {
        // Arrange – bez SendGrid job ma pójść w retry, a nie udawać udaną wysyłkę
        var sender = new LoggingEmailSender(new CapturingLogger(), Environment(Environments.Production));

        // Act
        var act = () => sender.SendAsync(EmailConfirmationMessage.Create("new@example.com", Link), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };

    private sealed class CapturingLogger : ILogger<LoggingEmailSender>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
