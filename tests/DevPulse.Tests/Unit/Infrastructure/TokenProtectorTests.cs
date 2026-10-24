using System.Security.Cryptography;
using DevPulse.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class TokenProtectorTests
{
    private const string AccessToken = "gho_16C7e42F292c6912E7710c838347Ae178B4a";

    private readonly EphemeralDataProtectionProvider _provider = new();

    [Fact]
    public void Should_ReturnOriginalToken_When_ProtectedValueUnprotected()
    {
        // Arrange
        var protector = new TokenProtector(_provider);

        // Act
        var protectedToken = protector.Protect(AccessToken);
        var unprotected = protector.Unprotect(protectedToken);

        // Assert
        protectedToken.Should().NotContain(AccessToken);
        unprotected.Should().Be(AccessToken);
    }

    [Fact]
    public void Should_ThrowCryptographicException_When_CiphertextTampered()
    {
        // Arrange – podmiana jednego znaku w środku payloadu (nadal poprawny base64url)
        var protector = new TokenProtector(_provider);
        var protectedToken = protector.Protect(AccessToken);
        var middle = protectedToken.Length / 2;
        var replacement = protectedToken[middle] == 'A' ? 'B' : 'A';
        var tampered = string.Concat(protectedToken[..middle], replacement.ToString(), protectedToken[(middle + 1)..]);

        // Act
        var act = () => protector.Unprotect(tampered);

        // Assert
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Should_ThrowCryptographicException_When_PayloadIsNotBase64Url()
    {
        // Arrange
        var protector = new TokenProtector(_provider);

        // Act
        var act = () => protector.Unprotect("to-nie-jest-payload!!");

        // Assert
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Should_RejectPayload_When_ProtectedWithDifferentPurpose()
    {
        // Arrange – np. payload cookie sesji nie może zostać odszyfrowany jako token GitHub
        var protector = new TokenProtector(_provider);
        var foreignPayload = _provider.CreateProtector("DevPulse.Other.v1").Protect(AccessToken);

        // Act
        var act = () => protector.Unprotect(foreignPayload);

        // Assert
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Should_Throw_When_TokenEmpty()
    {
        // Arrange
        var protector = new TokenProtector(_provider);

        // Act
        var act = () => protector.Protect(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
