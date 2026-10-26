using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using FluentAssertions;

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
}
