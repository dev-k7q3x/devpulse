using DevPulse.Application.DTOs.Auth;
using DevPulse.Application.Validators;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application.Validators;

[Trait("Category", "Unit")]
public class AuthLoginRequestValidatorTests
{
    private readonly AuthLoginRequestValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("/")]
    [InlineData("/settings?tab=email")]
    public void Should_Pass_When_ReturnUrlIsLocalOrMissing(string? returnUrl)
    {
        // Arrange
        var request = new AuthLoginRequest { ReturnUrl = returnUrl };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("settings")]
    [InlineData("")]
    public void Should_Fail_When_ReturnUrlIsNotLocal(string returnUrl)
    {
        // Arrange – ochrona przed open redirect
        var request = new AuthLoginRequest { ReturnUrl = returnUrl };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
