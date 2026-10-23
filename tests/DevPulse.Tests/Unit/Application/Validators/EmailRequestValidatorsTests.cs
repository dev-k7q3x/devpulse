using DevPulse.Application.DTOs.Settings;
using DevPulse.Application.Validators;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application.Validators;

[Trait("Category", "Unit")]
public class EmailChangeRequestValidatorTests
{
    private readonly EmailChangeRequestValidator _validator = new();

    [Fact]
    public void Should_Pass_When_EmailValid()
    {
        // Arrange
        var request = new EmailChangeRequest { Email = "octocat@example.com" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Should_Fail_When_EmailMissingOrInvalid(string email)
    {
        // Arrange
        var request = new EmailChangeRequest { Email = email };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_EmailLongerThanColumn()
    {
        // Arrange
        var request = new EmailChangeRequest { Email = new string('a', 250) + "@example.com" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public class EmailConfirmRequestValidatorTests
{
    private readonly EmailConfirmRequestValidator _validator = new();

    [Fact]
    public void Should_Pass_When_TokenIsBase64UrlOf32Bytes()
    {
        // Arrange
        var token = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var request = new EmailConfirmRequest { Token = token };

        // Act
        var result = _validator.Validate(request);

        // Assert
        token.Should().HaveLength(EmailConfirmRequestValidator.TokenLength);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void Should_Fail_When_TokenMalformed(string token)
    {
        // Arrange
        var request = new EmailConfirmRequest { Token = token };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
