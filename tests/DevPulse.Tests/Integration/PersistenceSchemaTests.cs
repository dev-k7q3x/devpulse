using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Domain.ValueObjects;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevPulse.Tests.Integration;

/// <summary>Schemat z migracji InitialCreate (plan 02): unikalne indeksy, CHECK-i, kaskady i mapowanie jsonb.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class PersistenceSchemaTests(DevPulseApiFactory factory)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    [Fact]
    public async Task Should_RejectSecondAccount_When_GitHubIdAlreadyExists()
    {
        // Arrange
        var first = new UserBuilder().Build();
        var duplicate = new UserBuilder().WithGitHubId(first.GitHubId).WithLogin("impostor").Build();
        await SaveAsync(first);

        // Act
        var act = () => SaveAsync(duplicate);

        // Assert
        await AssertPostgresErrorAsync(act, UniqueViolation, "ux_users_github_id");
    }

    [Fact]
    public async Task Should_RejectDuplicate_When_SameUserTracksRepositoryTwice()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        var duplicate = new TrackedRepositoryBuilder().ForUser(user.Id).WithGitHubRepoId(repository.GitHubRepoId).Build();
        await SaveAsync(user, repository);

        // Act
        var act = () => SaveAsync(duplicate);

        // Assert
        await AssertPostgresErrorAsync(act, UniqueViolation, "ux_tracked_repositories_user_id_github_repo_id");
    }

    [Fact]
    public async Task Should_AllowSameRepository_When_TrackedByDifferentUsers()
    {
        // Arrange – to samo repo u dwóch użytkowników to dwa wiersze (brak współdzielonego cache)
        var userA = new UserBuilder().Build();
        var userB = new UserBuilder().Build();
        var repositoryA = new TrackedRepositoryBuilder().ForUser(userA.Id).Build();
        var repositoryB = new TrackedRepositoryBuilder().ForUser(userB.Id).WithGitHubRepoId(repositoryA.GitHubRepoId).Build();

        // Act
        await SaveAsync(userA, userB, repositoryA, repositoryB);

        // Assert
        await using var db = factory.CreateDbContext();
        (await db.TrackedRepositories.CountAsync(r => r.GitHubRepoId == repositoryA.GitHubRepoId)).Should().Be(2);
    }

    [Fact]
    public async Task Should_RejectSecondDigest_When_SameUserAndLocalDate()
    {
        // Arrange – idempotencja digestu: drugi insert Pending kończy się unique violation
        var user = new UserBuilder().Build();
        var date = new DateOnly(2026, 10, 20);
        await SaveAsync(user, new DigestBuilder().ForUser(user.Id).ForDate(date).Build());
        var duplicate = new DigestBuilder().ForUser(user.Id).ForDate(date).Pending().Build();

        // Act
        var act = () => SaveAsync(duplicate);

        // Assert
        await AssertPostgresErrorAsync(act, UniqueViolation, "ux_digests_user_id_digest_date");
    }

    [Fact]
    public async Task Should_PersistMidnightSendHourAndFalseFlags_When_ValuesEqualClrDefaults()
    {
        // Arrange – godzina 0 i false to wartości domyślne CLR; nie mogą zostać zastąpione defaultami z bazy (7, true)
        var user = new UserBuilder().Build();
        var settings = new UserSettingsBuilder().ForUser(user.Id).WithSchedule(0, "Europe/Warsaw", digestEnabled: false).Build();

        // Act
        await SaveAsync(user, settings);

        // Assert
        await using var db = factory.CreateDbContext();
        var saved = await db.UserSettings.SingleAsync(s => s.UserId == user.Id);
        saved.SendHour.Value.Should().Be(0);
        saved.DigestEnabled.Should().BeFalse();
        saved.TimeZoneId.Should().Be("Europe/Warsaw");
    }

    [Fact]
    public async Task Should_ApplyDatabaseDefaults_When_SettingsInsertedWithRawSql()
    {
        // Arrange
        var user = new UserBuilder().Build();
        await SaveAsync(user);
        await using var db = factory.CreateDbContext();

        // Act
        await db.Database.ExecuteSqlAsync($"INSERT INTO user_settings (user_id, updated_at) VALUES ({user.Id}, now())");

        // Assert
        var saved = await db.UserSettings.SingleAsync(s => s.UserId == user.Id);
        saved.SendHour.Should().Be(SendHour.Default);
        saved.TimeZoneId.Should().Be("UTC");
        saved.DigestEnabled.Should().BeTrue();
        saved.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Should_RejectSendHour_When_OutsideZeroToTwentyThree()
    {
        // Arrange
        var user = new UserBuilder().Build();
        await SaveAsync(user, new UserSettingsBuilder().ForUser(user.Id).Build());
        await using var db = factory.CreateDbContext();

        // Act
        var act = () => db.Database.ExecuteSqlAsync($"UPDATE user_settings SET send_hour = 24 WHERE user_id = {user.Id}");

        // Assert
        await AssertPostgresErrorAsync(act, CheckViolation, "ck_user_settings_send_hour");
    }

    [Fact]
    public async Task Should_RejectPendingEmail_When_TokenHashMissing()
    {
        // Arrange
        var user = new UserBuilder().Build();
        await SaveAsync(user, new UserSettingsBuilder().ForUser(user.Id).Build());
        await using var db = factory.CreateDbContext();

        // Act
        var act = () => db.Database.ExecuteSqlAsync(
            $"UPDATE user_settings SET pending_email = 'new@example.com' WHERE user_id = {user.Id}");

        // Assert
        await AssertPostgresErrorAsync(act, CheckViolation, "ck_user_settings_pending_email_token");
    }

    [Fact]
    public async Task Should_RejectSentStatus_When_SentAtMissing()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var digest = new DigestBuilder().ForUser(user.Id).Pending().Build();
        await SaveAsync(user, digest);
        await using var db = factory.CreateDbContext();

        // Act
        var act = () => db.Database.ExecuteSqlAsync($"UPDATE digests SET status = 'Sent' WHERE id = {digest.Id}");

        // Assert
        await AssertPostgresErrorAsync(act, CheckViolation, "ck_digests_sent_at");
    }

    [Fact]
    public async Task Should_RejectUnknownCiStatus_When_WrittenWithRawSql()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SaveAsync(user, repository, new RepositorySnapshotBuilder().ForRepository(repository.Id).Build());
        await using var db = factory.CreateDbContext();

        // Act
        var act = () => db.Database.ExecuteSqlAsync(
            $"UPDATE repository_snapshots SET ci_status = 'Exploded' WHERE tracked_repository_id = {repository.Id}");

        // Assert
        await AssertPostgresErrorAsync(act, CheckViolation, "ck_repository_snapshots_ci_status");
    }

    [Fact]
    public async Task Should_RoundTripSnapshotJsonAndEnumText_When_Saved()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        var snapshot = new RepositorySnapshotBuilder().ForRepository(repository.Id).WithCi(CiStatus.Failure).Build();

        // Act
        await SaveAsync(user, repository, snapshot);

        // Assert
        await using var db = factory.CreateDbContext();
        var saved = await db.RepositorySnapshots.SingleAsync(s => s.TrackedRepositoryId == repository.Id);
        saved.CiStatus.Should().Be(CiStatus.Failure);
        saved.OpenPullRequests.Should().BeEquivalentTo(snapshot.OpenPullRequests);

        var ciStatusText = await db.Database
            .SqlQuery<string>($"SELECT ci_status AS \"Value\" FROM repository_snapshots WHERE tracked_repository_id = {repository.Id}")
            .SingleAsync();
        ciStatusText.Should().Be("Failure");
    }

    [Fact]
    public async Task Should_RoundTripDigestPrioritiesAndSummary_When_Saved()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var digest = new DigestBuilder().ForUser(user.Id).Build();

        // Act
        await SaveAsync(user, digest);

        // Assert
        await using var db = factory.CreateDbContext();
        var saved = await db.Digests.SingleAsync(d => d.Id == digest.Id);
        saved.Status.Should().Be(DigestStatus.Sent);
        saved.Priorities.Should().BeEquivalentTo(digest.Priorities);
        saved.RepositorySummary.Should().BeEquivalentTo(digest.RepositorySummary);
        saved.AiUsed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_DeleteAllUserData_When_UserDeleted()
    {
        // Arrange – usunięcie konta czyści wszystko jednym DELETE (ON DELETE CASCADE)
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SaveAsync(
            user,
            new UserSettingsBuilder().ForUser(user.Id).Build(),
            repository,
            new RepositorySnapshotBuilder().ForRepository(repository.Id).Build(),
            new DigestBuilder().ForUser(user.Id).Build());
        await using var db = factory.CreateDbContext();

        // Act
        await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();

        // Assert
        (await db.UserSettings.AnyAsync(s => s.UserId == user.Id)).Should().BeFalse();
        (await db.TrackedRepositories.AnyAsync(r => r.UserId == user.Id)).Should().BeFalse();
        (await db.RepositorySnapshots.AnyAsync(s => s.TrackedRepositoryId == repository.Id)).Should().BeFalse();
        (await db.Digests.AnyAsync(d => d.UserId == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Should_StoreDataProtectionKey_When_KeyRingPersisted()
    {
        // Arrange
        var key = new DataProtectionKey { FriendlyName = $"key-{Guid.NewGuid()}", Xml = "<key />" };

        // Act
        await SaveAsync(key);

        // Assert
        await using var db = factory.CreateDbContext();
        (await db.DataProtectionKeys.AnyAsync(k => k.FriendlyName == key.FriendlyName)).Should().BeTrue();
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static async Task AssertPostgresErrorAsync(Func<Task> act, string sqlState, string constraintName)
    {
        var exception = await act.Should().ThrowAsync<Exception>();
        var postgresException = exception.Which as PostgresException ?? exception.Which.InnerException as PostgresException;

        postgresException.Should().NotBeNull("oczekiwano błędu PostgreSQL, a był {0}", exception.Which.GetType().Name);
        postgresException!.SqlState.Should().Be(sqlState);
        postgresException.ConstraintName.Should().Be(constraintName);
    }
}
