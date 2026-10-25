using System.Net;
using System.Text.Json;
using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

[Trait("Category", "Unit")]
public class SendGridEmailSenderTests
{
    private const string ApiKey = "SG.test-key";

    private static readonly EmailMessage Message = new(
        "octocat@example.com",
        "DevPulse – 21.10.2026: wszystkie repozytoria w porządku",
        "<p>HTML</p>",
        "Tekst");

    [Fact]
    public async Task Should_ReturnMessageId_When_SendGridAcceptsEmail()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.Accepted,
            configure: response => response.Headers.Add(SendGridEmailSender.MessageIdHeader, "msg-123abc"));

        // Act
        var messageId = await CreateSender(handler).SendAsync(Message, CancellationToken.None);

        // Assert
        messageId.Should().Be("msg-123abc");
    }

    [Fact]
    public async Task Should_SendAuthorizedMailSendPayload_When_Sending()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Accepted);

        // Act
        await CreateSender(handler).SendAsync(Message, CancellationToken.None);

        // Assert
        var request = handler.LastRequest!;
        request.RequestUri.Should().Be(new Uri("https://api.sendgrid.com/v3/mail/send"));
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(ApiKey);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;
        root.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString().Should().Be("octocat@example.com");
        root.GetProperty("from").GetProperty("email").GetString().Should().Be("digest@devpulse.example");
        root.GetProperty("from").GetProperty("name").GetString().Should().Be("DevPulse");
        root.GetProperty("subject").GetString().Should().Be(Message.Subject);
        root.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("type").GetString())
            .Should().Equal("text/plain", "text/html");
        root.GetProperty("mail_settings").GetProperty("sandbox_mode").GetProperty("enable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Should_EnableSandboxAndSkipPlainText_When_ConfiguredAndNoTextBody()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var htmlOnly = Message with { PlainTextBody = null };

        // Act
        var messageId = await CreateSender(handler, sandboxMode: true).SendAsync(htmlOnly, CancellationToken.None);

        // Assert
        messageId.Should().BeNull();
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("mail_settings").GetProperty("sandbox_mode").GetProperty("enable").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("content").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task Should_ThrowDeliveryExceptionWithoutRecipient_When_SendGridRejects(HttpStatusCode status, bool expectedTransient)
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(status, StubHttpMessageHandler.Fixture("SendGrid", "error-bad-request.json"));

        // Act
        var act = () => CreateSender(handler).SendAsync(Message, CancellationToken.None);

        // Assert
        var exception = (await act.Should().ThrowAsync<EmailDeliveryException>()).Which;
        exception.StatusCode.Should().Be(status);
        exception.IsTransient.Should().Be(expectedTransient);
        exception.Message.Should().NotContain("octocat@example.com").And.NotContain("Sender Identity");
    }

    [Theory]
    [InlineData(null, "digest@devpulse.example")]
    [InlineData(ApiKey, "")]
    public async Task Should_ThrowWithoutCallingApi_When_ConfigurationIncomplete(string? apiKey, string fromEmail)
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Accepted);
        var sender = new SendGridEmailSender(
            new HttpClient(handler),
            Options.Create(new SendGridOptions { ApiKey = apiKey, FromEmail = fromEmail }),
            NullLogger<SendGridEmailSender>.Instance);

        // Act
        var act = () => sender.SendAsync(Message, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SendGrid__ApiKey*");
        handler.CallCount.Should().Be(0);
    }

    private static SendGridEmailSender CreateSender(StubHttpMessageHandler handler, bool sandboxMode = false) => new(
        new HttpClient(handler),
        Options.Create(new SendGridOptions { ApiKey = ApiKey, FromEmail = "digest@devpulse.example", SandboxMode = sandboxMode }),
        NullLogger<SendGridEmailSender>.Instance);
}
