using DevPulse.Application.Errors;
using DevPulse.Application.Models.GitHub;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>SnapshotRefreshJob na prawdziwym PostgreSQL z fake IGitHubClient – bez sieci i bez serwera Hangfire.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class SnapshotRefreshJobTests(DevPulseApiFactory factory)
{
    private static readonly DateTimeOffset Now = TestClock.Now.AddDays(1);

    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly StateGitHubClient _gitHub = new();
    private readonly RecordingBackgroundJobClient _jobs = new();

    [Fact]
    public async Task Should_CreateSnapshotAndFollowRename_When_GitHubReturnsState()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).WithFullName("acme", "old-name").Build();
        await SeedAsync(user, repository);
        _gitHub.States = ids => ids.Select(id => new GitHubRepositoryStateResult(
            id, StateGitHubClient.State(id, owner: "acme", name: "new-name", openPrCount: 3, openIssueCount: 4), null)).ToList();

        // Act
        await CreateJob().RefreshUserAsync(user.Id, CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        var snapshot = await db.RepositorySnapshots.SingleAsync(s => s.TrackedRepositoryId == repository.Id);
        snapshot.OpenPrCount.Should().Be(3);
        snapshot.OpenIssueCount.Should().Be(4);
        snapshot.CiStatus.Should().Be(CiStatus.Failure);
        snapshot.CiCommitSha.Should().Be(new string('b', 40));
        snapshot.OpenPullRequests.Should().ContainSingle().Which.Number.Should().Be(7);
        snapshot.RefreshedAt.Should().Be(Now);
        snapshot.LastError.Should().BeNull();

        var tracked = await db.TrackedRepositories.SingleAsync(r => r.Id == repository.Id);
        tracked.FullName.Should().Be("acme/new-name", "repo identyfikujemy po node id – nazwa może się zmienić");
    }

    [Fact]
    public async Task Should_OverwriteExistingSnapshot_When_RefreshSucceeds()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SeedAsync(user, repository, new RepositorySnapshotBuilder().ForRepository(repository.Id).WithCounts(1, 1).WithLastError("Unavailable").Build());
        _gitHub.States = ids => ids.Select(id => new GitHubRepositoryStateResult(id, StateGitHubClient.State(id, openPrCount: 9), null)).ToList();

        // Act
        await CreateJob().RefreshUserAsync(user.Id, CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        var snapshot = await db.RepositorySnapshots.SingleAsync(s => s.TrackedRepositoryId == repository.Id);
        snapshot.OpenPrCount.Should().Be(9);
        snapshot.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Should_KeepPreviousDataAndRecordError_When_RepositoryNoLongerAccessible()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SeedAsync(user, repository, new RepositorySnapshotBuilder().ForRepository(repository.Id).WithCounts(2, 5).Build());
        _gitHub.States = ids => ids.Select(id => new GitHubRepositoryStateResult(id, null, "Forbidden")).ToList();

        // Act
        await CreateJob().RefreshUserAsync(user.Id, CancellationToken.None);

        // Assert – stare dane lepsze niż puste
        await using var db = factory.CreateDbContext();
        var snapshot = await db.RepositorySnapshots.SingleAsync(s => s.TrackedRepositoryId == repository.Id);
        snapshot.OpenPrCount.Should().Be(2);
        snapshot.OpenIssueCount.Should().Be(5);
        snapshot.RefreshedAt.Should().Be(TestClock.Now);
        snapshot.LastAttemptAt.Should().Be(Now);
        snapshot.LastError.Should().Be("Forbidden");
    }

    [Theory]
    [InlineData("unavailable", "Unavailable")]
    [InlineData("rate-limited", "RateLimited")]
    [InlineData("token-invalid", "TokenInvalid")]
    public async Task Should_KeepPreviousDataAndNotThrow_When_GitHubCallFails(string failure, string expectedError)
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SeedAsync(user, repository, new RepositorySnapshotBuilder().ForRepository(repository.Id).WithCounts(2, 5).Build());
        _gitHub.States = _ => throw failure switch
        {
            "unavailable" => new GitHubUnavailableException(),
            "rate-limited" => new GitHubRateLimitedException(TimeSpan.FromMinutes(5)),
            _ => new GitHubTokenInvalidException(),
        };

        // Act
        var act = () => CreateJob().RefreshUserAsync(user.Id, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync("kolejne odświeżenie przyjdzie z harmonogramu, bez ponowień Hangfire");
        await using var db = factory.CreateDbContext();
        var snapshot = await db.RepositorySnapshots.SingleAsync(s => s.TrackedRepositoryId == repository.Id);
        snapshot.OpenPrCount.Should().Be(2);
        snapshot.LastError.Should().Be(expectedError);
    }

    [Fact]
    public async Task Should_NotCreateSnapshot_When_FirstRefreshFails()
    {
        // Arrange – bez danych karta zostaje „Loading”, zamiast pokazywać zera
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SeedAsync(user, repository);

        // Act
        await CreateJob().RefreshRepositoryAsync(user.Id, repository.Id, CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        (await db.RepositorySnapshots.AnyAsync(s => s.TrackedRepositoryId == repository.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Should_RequestOnlyGivenRepository_When_RefreshingSingleRepository()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var first = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        var second = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await SeedAsync(user, first, second);

        // Act
        await CreateJob().RefreshRepositoryAsync(user.Id, second.Id, CancellationToken.None);

        // Assert
        _gitHub.RequestedNodeIds.Should().ContainSingle().Which.Should().Equal(second.GitHubNodeId);
    }

    [Fact]
    public async Task Should_IgnoreRepository_When_ItBelongsToAnotherUser()
    {
        // Arrange – izolacja: userId z joba i id repo innego użytkownika
        var owner = new UserBuilder().Build();
        var other = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(owner.Id).Build();
        await SeedAsync(owner, other, repository);

        // Act
        await CreateJob().RefreshRepositoryAsync(other.Id, repository.Id, CancellationToken.None);

        // Assert
        _gitHub.RequestedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_ScheduleOnlyActiveUsersWithRepositoriesAndValidToken_When_RefreshAllRuns()
    {
        // Arrange – okno aktywności 14 dni liczone od Now
        var recentLogin = new UserBuilder().At(Now.AddDays(-1)).Build();
        var digestOnly = new UserBuilder().At(Now.AddDays(-30)).Build();
        var inactive = new UserBuilder().At(Now.AddDays(-30)).Build();
        var invalidToken = new UserBuilder().At(Now.AddDays(-1)).WithInvalidToken().Build();
        var withoutRepositories = new UserBuilder().At(Now.AddDays(-1)).Build();
        await SeedAsync(
            recentLogin,
            digestOnly,
            inactive,
            invalidToken,
            withoutRepositories,
            new UserSettingsBuilder().ForUser(recentLogin.Id).WithSchedule(7, "UTC", digestEnabled: false).Build(),
            new UserSettingsBuilder().ForUser(digestOnly.Id).WithSchedule(7, "UTC", digestEnabled: true).Build(),
            new UserSettingsBuilder().ForUser(inactive.Id).WithSchedule(7, "UTC", digestEnabled: false).Build(),
            new TrackedRepositoryBuilder().ForUser(recentLogin.Id).Build(),
            new TrackedRepositoryBuilder().ForUser(digestOnly.Id).Build(),
            new TrackedRepositoryBuilder().ForUser(inactive.Id).Build(),
            new TrackedRepositoryBuilder().ForUser(invalidToken.Id).Build());

        // Act
        await CreateJob().RefreshAllAsync(CancellationToken.None);

        // Assert – baza jest współdzielona z innymi testami, więc sprawdzamy tylko własnych użytkowników
        var scheduled = _jobs.Jobs
            .Where(j => j.Method.Name == nameof(SnapshotRefreshJob.RefreshUserAsync))
            .Select(j => (Guid)j.Args[0]!)
            .ToHashSet();
        scheduled.Should().Contain([recentLogin.Id, digestOnly.Id]);
        scheduled.Should().NotContain([inactive.Id, invalidToken.Id, withoutRepositories.Id]);
    }

    private SnapshotRefreshJob CreateJob() => new(
        factory.Services.GetRequiredService<IDbContextFactory<DevPulseDbContext>>(),
        _gitHub,
        _jobs,
        _timeProvider,
        Options.Create(new SnapshotOptions()),
        NullLogger<SnapshotRefreshJob>.Instance);

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
