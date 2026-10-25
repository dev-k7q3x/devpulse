using System.Security.Cryptography;
using DevPulse.Application.Settings;
using DevPulse.Application.Validators;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class EmailConfirmationTokenTests
{
    [Fact]
    public void Should_GenerateBase64UrlTokenMatchingValidatorFormat_When_Generated()
    {
        // Act
        var (token, hash) = EmailConfirmationToken.Generate();

        // Assert – 32 bajty = 43 znaki base64url bez paddingu, hash SHA-256 = 32 bajty
        token.Should().HaveLength(EmailConfirmRequestValidator.TokenLength).And.MatchRegex("^[A-Za-z0-9_-]+$");
        hash.Should().HaveCount(SHA256.HashSizeInBytes);
    }

    [Fact]
    public void Should_ReturnSameHash_When_GeneratedTokenHashedAgain()
    {
        // Arrange
        var (token, hash) = EmailConfirmationToken.Generate();

        // Act
        var recomputed = EmailConfirmationToken.TryHash(token);

        // Assert
        recomputed.Should().Equal(hash);
    }

    [Fact]
    public void Should_GenerateDifferentTokens_When_CalledTwice()
    {
        // Act
        var first = EmailConfirmationToken.Generate();
        var second = EmailConfirmationToken.Generate();

        // Assert
        first.Token.Should().NotBe(second.Token);
        first.Hash.Should().NotEqual(second.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("za-krotki")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Should_ReturnNull_When_TokenIsNotBase64UrlOf32Bytes(string? token)
    {
        // Act
        var hash = EmailConfirmationToken.TryHash(token);

        // Assert
        hash.Should().BeNull();
    }
}
