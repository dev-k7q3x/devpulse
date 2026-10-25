using DevPulse.Application.Errors;
using DevPulse.Domain.Entities;
using DevPulse.Infrastructure.GitHub;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Infrastructure.Security;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>Token GitHub dla klienta API: odszyfrowanie z bazy i oznaczanie jako nieważny (prawdziwy PostgreSQL).</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class GitHubTokenStoreTests(DevPulseApiFactory factory)
{
    private const string AccessToken = "gho_tokenStoreTest";

    private readonly TokenProtector _tokenProtector = new(new EphemeralDataProtectionProvider());
    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now.AddDays(1));

    [Fact]
    public async Task Should_ReturnDecryptedToken_When_UserHasValidToken()
    {
        // Arrange
        var user = UserWithEncryptedToken();
        await SeedAsync(user);

        // Act
        var token = await CreateStore().GetAccessTokenAsync(user.Id, CancellationToken.None);

        // Assert
        token.Should().Be(AccessToken);
    }

    [Fact]
    public async Task Should_Throw_When_TokenAlreadyMarkedInvalid()
    {
        // Arrange
        var user = UserWithEncryptedToken();
        user.MarkTokenInvalid(TestClock.Now);
        await SeedAsync(user);

        // Act
        var act = () => CreateStore().GetAccessTokenAsync(user.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubTokenInvalidException>();
    }

    [Fact]
    public async Task Should_Throw_When_UserDoesNotExist()
    {
        // Act
        var act = () => CreateStore().GetAccessTokenAsync(Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubTokenInvalidException>();
    }

    [Fact]
    public async Task Should_MarkTokenInvalidAndThrow_When_TokenCannotBeDecrypted()
    {
        // Arrange – np. utracony pierścień kluczy: użytkownik wypada z odświeżania do ponownego logowania
        var user = new UserBuilder().Build();
        await SeedAsync(user);

        // Act
        var act = () => CreateStore().GetAccessTokenAsync(user.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubTokenInvalidException>();
        await using var db = factory.CreateDbContext();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).TokenInvalid.Should().BeTrue();
    }

    [Fact]
    public async Task Should_SetTokenInvalidFlag_When_MarkedInvalid()
    {
        // Arrange
        var user = new UserBuilder().Build();
        await SeedAsync(user);

        // Act
        await CreateStore().MarkTokenInvalidAsync(user.Id, CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        var updated = await db.Users.SingleAsync(u => u.Id == user.Id);
        updated.TokenInvalid.Should().BeTrue();
        updated.UpdatedAt.Should().Be(TestClock.Now.AddDays(1));
    }

    [Fact]
    public async Task Should_NotThrow_When_MarkingUnknownUser()
    {
        // Act
        var act = () => CreateStore().MarkTokenInvalidAsync(Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>Token zaszyfrowany tym samym protectorem co w store (builder ustawia atrapę, której nie da się odszyfrować).</summary>
    private User UserWithEncryptedToken()
    {
        var user = new UserBuilder().Build();
        user.RecordLogin(user.Login, user.AvatarUrl, _tokenProtector.Protect(AccessToken), user.TokenScopes, TestClock.Now);
        return user;
    }

    private GitHubTokenStore CreateStore() => new(
        factory.Services.GetRequiredService<IDbContextFactory<DevPulseDbContext>>(),
        _tokenProtector,
        _timeProvider,
        NullLogger<GitHubTokenStore>.Instance);

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
