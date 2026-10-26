using DevPulse.Application.Options;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary><see cref="RetentionJob"/> na prawdziwym PostgreSQL – <c>ExecuteDeleteAsync</c> po <c>created_at</c> dla wszystkich użytkowników.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class RetentionJobTests(DevPulseApiFactory factory)
{
    // Digesty innych testów kolekcji powstają w TestClock.Now (świeże), więc próg ich nie usuwa.
    private static readonly DateTimeOffset Now = TestClock.Now;

    [Fact]
    public async Task Should_DeleteOnlyDigestsOlderThanWindow_When_JobRuns()
    {
        // Arrange – dwóch użytkowników: stare digesty obu znikają (job globalny), świeże zostają
        var userA = new UserBuilder().Build();
        var userB = new UserBuilder().Build();
        var oldA = new DigestBuilder().ForUser(userA.Id).ForDate(new DateOnly(2026, 9, 1)).At(Now.AddDays(-31)).Build();
        var oldB = new DigestBuilder().ForUser(userB.Id).ForDate(new DateOnly(2026, 9, 2)).At(Now.AddDays(-45)).Build();
        var recentA = new DigestBuilder().ForUser(userA.Id).ForDate(new DateOnly(2026, 9, 25)).At(Now.AddDays(-29)).Build();
        await SeedAsync(userA, userB, oldA, oldB, recentA);

        // Act
        var deleted = await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        var remaining = await db.Digests.Where(d => d.UserId == userA.Id || d.UserId == userB.Id).Select(d => d.Id).ToListAsync();
        remaining.Should().Equal(recentA.Id);
        deleted.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Should_KeepDigest_When_CreatedExactlyAtCutoff()
    {
        // Arrange – warunek to created_at < cutoff (ścisła nierówność)
        var user = new UserBuilder().Build();
        var boundary = new DigestBuilder().ForUser(user.Id).ForDate(new DateOnly(2026, 9, 21)).At(Now.AddDays(-30)).Build();
        await SeedAsync(user, boundary);

        // Act
        await CreateJob().RunAsync(CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        (await db.Digests.AnyAsync(d => d.Id == boundary.Id)).Should().BeTrue();
    }

    private RetentionJob CreateJob() => new(
        factory.Services.GetRequiredService<IDbContextFactory<DevPulseDbContext>>(),
        new FakeTimeProvider(Now),
        Options.Create(new DigestHistoryOptions { RetentionPeriod = TimeSpan.FromDays(30) }),
        NullLogger<RetentionJob>.Instance);

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
