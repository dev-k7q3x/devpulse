using DevPulse.Application.Models.GitHub;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Infrastructure.Security;
using DevPulse.Infrastructure.Services;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>
/// AuthService na prawdziwym PostgreSQL – upsert, projekcja i ExecuteDelete z kaskadą zależą od bazy,
/// więc test bez I/O (mock DbSet) nie sprawdzałby niczego istotnego.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class AuthServiceTests(DevPulseApiFactory factory)
{
    private const string AccessToken = "gho_new_access_token";

    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);
    private readonly TokenProtector _tokenProtector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Should_CreateUserWithConfirmedEmailAndEncryptedToken_When_FirstSignIn()
    {
        // Arrange
        var gitHubId = UniqueGitHubId();
        var login = new GitHubLogin(gitHubId, "new-user", "https://avatars.example/1", "new@example.com", AccessToken, "repo user:email");
        await using var db = factory.CreateDbContext();

        // Act
        var userId = await CreateService(db).SignInWithGitHubAsync(login, CancellationToken.None);

        // Assert
        await using var assertDb = factory.CreateDbContext();
        var user = await assertDb.Users.SingleAsync(u => u.Id == userId);
        user.GitHubId.Should().Be(gitHubId);
        user.Login.Should().Be("new-user");
        user.TokenScopes.Should().Be("repo,user:email");
        user.AccessTokenEncrypted.Should().NotContain(AccessToken);
        _tokenProtector.Unprotect(user.AccessTokenEncrypted).Should().Be(AccessToken);
        user.LastLoginAt.Should().Be(TestClock.Now);

        var settings = await assertDb.UserSettings.SingleAsync(s => s.UserId == userId);
        settings.Email.Should().Be("new@example.com");
        settings.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_UpdateProfileAndKeepSettings_When_ExistingUserSignsInWithChangedLogin()
    {
        // Arrange – identyfikacja po id GitHuba: zmiana loginu nie tworzy nowego konta
        var user = new UserBuilder().WithLogin("old-login").WithInvalidToken().Build();
        await SeedAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithEmail("chosen@example.com").Build());
        _timeProvider.Advance(TimeSpan.FromDays(3));
        var login = new GitHubLogin(user.GitHubId, "new-login", null, "github-primary@example.com", AccessToken, "repo,read:user,user:email");
        await using var db = factory.CreateDbContext();

        // Act
        var userId = await CreateService(db).SignInWithGitHubAsync(login, CancellationToken.None);

        // Assert
        userId.Should().Be(user.Id);
        await using var assertDb = factory.CreateDbContext();
        var updated = await assertDb.Users.SingleAsync(u => u.GitHubId == user.GitHubId);
        updated.Login.Should().Be("new-login");
        updated.TokenInvalid.Should().BeFalse();
        updated.LastLoginAt.Should().Be(TestClock.Now.AddDays(3));
        _tokenProtector.Unprotect(updated.AccessTokenEncrypted).Should().Be(AccessToken);
        (await assertDb.UserSettings.SingleAsync(s => s.UserId == user.Id)).Email.Should().Be("chosen@example.com");
    }

    [Fact]
    public async Task Should_ReturnProfileWithTokenStateAndMissingScopes_When_UserExists()
    {
        // Arrange
        var user = new UserBuilder().WithLogin("Admin-User").WithTokenScopes("repo").WithInvalidToken().Build();
        await SeedAsync(user);
        await using var db = factory.CreateDbContext();

        // Act
        var result = await CreateService(db, adminLogins: ["admin-user"]).GetCurrentUserAsync(user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
        result.Login.Should().Be("Admin-User");
        result.GitHubTokenValid.Should().BeFalse();
        result.MissingScopes.Should().Equal("read:user", "user:email");
        result.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Should_ReturnNull_When_UserDoesNotExist()
    {
        // Arrange
        await using var db = factory.CreateDbContext();

        // Act
        var result = await CreateService(db).GetCurrentUserAsync(Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Should_DeleteOnlyRequestedUserWithDependentRows_When_AccountDeleted()
    {
        // Arrange – użytkownik B zostaje nietknięty
        var userA = new UserBuilder().Build();
        var userB = new UserBuilder().Build();
        var repositoryA = new TrackedRepositoryBuilder().ForUser(userA.Id).Build();
        await SeedAsync(
            userA,
            userB,
            new UserSettingsBuilder().ForUser(userA.Id).Build(),
            new UserSettingsBuilder().ForUser(userB.Id).Build(),
            repositoryA,
            new RepositorySnapshotBuilder().ForRepository(repositoryA.Id).Build(),
            new DigestBuilder().ForUser(userA.Id).Build(),
            new TrackedRepositoryBuilder().ForUser(userB.Id).Build());
        await using var db = factory.CreateDbContext();

        // Act
        await CreateService(db).DeleteAccountAsync(userA.Id, CancellationToken.None);

        // Assert
        await using var assertDb = factory.CreateDbContext();
        (await assertDb.Users.AnyAsync(u => u.Id == userA.Id)).Should().BeFalse();
        (await assertDb.UserSettings.AnyAsync(s => s.UserId == userA.Id)).Should().BeFalse();
        (await assertDb.TrackedRepositories.AnyAsync(r => r.UserId == userA.Id)).Should().BeFalse();
        (await assertDb.Digests.AnyAsync(d => d.UserId == userA.Id)).Should().BeFalse();
        (await assertDb.Users.AnyAsync(u => u.Id == userB.Id)).Should().BeTrue();
        (await assertDb.TrackedRepositories.AnyAsync(r => r.UserId == userB.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Should_NotThrow_When_DeletingNonExistentAccount()
    {
        // Arrange
        await using var db = factory.CreateDbContext();

        // Act
        var act = () => CreateService(db).DeleteAccountAsync(Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    private AuthService CreateService(DevPulseDbContext db, IReadOnlyList<string>? adminLogins = null) => new(
        db,
        _tokenProtector,
        _timeProvider,
        Options.Create(new AdminOptions { Logins = adminLogins ?? [] }),
        NullLogger<AuthService>.Instance);

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static long UniqueGitHubId() => new UserBuilder().Build().GitHubId;
}
