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

/// <summary>
/// DigestSchedulerJob na prawdziwym PostgreSQL. Baza jest wspólna dla kolekcji testów, więc asercje dotyczą
/// wyłącznie użytkowników utworzonych w danym teście.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class DigestSchedulerJobTests(DevPulseApiFactory factory)
{
    private readonly RecordingBackgroundJobClient _jobs = new();

    [Fact]
    public async Task Should_EnqueueUsersWhoseLocalSendHourIsNow_When_UsersInThreeTimeZones()
    {
        // Arrange – 2026-10-21 05:00 UTC = 07:00 w Warszawie (CEST) = 01:00 w Nowym Jorku (EDT)
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var utc = await SeedUserAsync(5, "UTC");
        var warsaw = await SeedUserAsync(7, "Europe/Warsaw");
        var newYork = await SeedUserAsync(1, "America/New_York");
        var notYet = await SeedUserAsync(9, "UTC");
        var warsawAtFive = await SeedUserAsync(5, "Europe/Warsaw");

        // Act
        await CreateJob(now).RunAsync(CancellationToken.None);

        // Assert
        var scheduled = ScheduledFor(utc, warsaw, newYork, notYet, warsawAtFive);
        scheduled.Should().BeEquivalentTo(
        [
            (utc, new DateOnly(2026, 10, 21)),
            (warsaw, new DateOnly(2026, 10, 21)),
            (newYork, new DateOnly(2026, 10, 21)),
        ]);
    }

    [Fact]
    public async Task Should_SkipUser_When_DigestDisabledOrEmailMissingOrTokenInvalidOrNoRepositories()
    {
        // Arrange – wszyscy mają godzinę wysyłki teraz
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var eligible = await SeedUserAsync(5, "UTC");
        var disabled = await SeedUserAsync(5, "UTC", digestEnabled: false);
        var withoutEmail = await SeedUserAsync(5, "UTC", email: null);
        var invalidToken = await SeedUserAsync(5, "UTC", tokenInvalid: true);
        var withoutRepositories = await SeedUserAsync(5, "UTC", withRepository: false);

        // Act
        await CreateJob(now).RunAsync(CancellationToken.None);

        // Assert
        var scheduled = ScheduledFor(eligible, disabled, withoutEmail, invalidToken, withoutRepositories);
        scheduled.Should().Equal((eligible, new DateOnly(2026, 10, 21)));
    }

    [Fact]
    public async Task Should_NotEnqueue_When_DigestForLocalDateAlreadyExists()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var alreadySent = await SeedUserAsync(5, "UTC");
        await SeedAsync(new DigestBuilder().ForUser(alreadySent).ForDate(new DateOnly(2026, 10, 21)).At(now.AddMinutes(-30)).Build());

        // Act
        await CreateJob(now).RunAsync(CancellationToken.None);

        // Assert
        ScheduledFor(alreadySent).Should().BeEmpty();
    }

    [Fact]
    public async Task Should_EnqueueOnce_When_SendHourRepeatsOnLastSundayOfOctober()
    {
        // Arrange – 2026-10-25 w Warszawie 02:00 występuje o 00:00 UTC (CEST) i ponownie o 01:00 UTC (CET)
        var user = await SeedUserAsync(2, "Europe/Warsaw");

        // Act
        await CreateJob(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)).RunAsync(CancellationToken.None);
        await CreateJob(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero)).RunAsync(CancellationToken.None);

        // Assert
        ScheduledFor(user).Should().Equal((user, new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public async Task Should_SkipUserWithoutFailing_When_TimeZoneUnknown()
    {
        // Arrange – strefa spoza bazy IANA na tym systemie nie może zatrzymać schedulera dla innych
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var unknownZone = await SeedUserAsync(5, "Mars/Olympus_Mons");
        var eligible = await SeedUserAsync(5, "UTC");

        // Act
        await CreateJob(now).RunAsync(CancellationToken.None);

        // Assert
        ScheduledFor(unknownZone, eligible).Should().Equal((eligible, new DateOnly(2026, 10, 21)));
    }

    [Fact]
    public async Task Should_RequeueOnlyRecentlyAbandonedPendingDigests_When_SchedulerRuns()
    {
        // Arrange – godzina wysyłki nikomu teraz nie wypada; trzy wiersze Pending w różnym wieku
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var abandoned = await SeedUserAsync(7, "UTC");
        var inProgress = await SeedUserAsync(7, "UTC");
        var tooOld = await SeedUserAsync(7, "UTC");
        var disabledMeanwhile = await SeedUserAsync(7, "UTC", digestEnabled: false);
        var invalidToken = await SeedUserAsync(7, "UTC", tokenInvalid: true);
        await SeedAsync(
            new DigestBuilder().ForUser(invalidToken).ForDate(new DateOnly(2026, 10, 21)).Pending().At(now.AddMinutes(-40)).Build(),
            new DigestBuilder().ForUser(abandoned).ForDate(new DateOnly(2026, 10, 21)).Pending().At(now.AddMinutes(-40)).Build(),
            new DigestBuilder().ForUser(disabledMeanwhile).ForDate(new DateOnly(2026, 10, 21)).Pending().At(now.AddMinutes(-40)).Build(),
            new DigestBuilder().ForUser(inProgress).ForDate(new DateOnly(2026, 10, 21)).Pending().At(now.AddMinutes(-5)).Build(),
            new DigestBuilder().ForUser(tooOld).ForDate(new DateOnly(2026, 10, 19)).Pending().At(now.AddDays(-2)).Build());

        // Act
        await CreateJob(now).RunAsync(CancellationToken.None);

        // Assert
        ScheduledFor(abandoned, inProgress, tooOld, disabledMeanwhile, invalidToken).Should().Equal((abandoned, new DateOnly(2026, 10, 21)));
    }

    private DigestSchedulerJob CreateJob(DateTimeOffset now) => new(
        factory.Services.GetRequiredService<IDbContextFactory<DevPulseDbContext>>(),
        _jobs,
        new FakeTimeProvider(now),
        Options.Create(new DigestPipelineOptions()),
        NullLogger<DigestSchedulerJob>.Instance);

    /// <summary>Joby SendDigestJob enqueue'owane dla podanych użytkowników (pary userId + data).</summary>
    private List<(Guid UserId, DateOnly DigestDate)> ScheduledFor(params Guid[] userIds) => _jobs.Jobs
        .Where(job => job.Type == typeof(SendDigestJob) && job.Method.Name == nameof(SendDigestJob.SendAsync))
        .Select(job => ((Guid)job.Args[0]!, (DateOnly)job.Args[1]!))
        .Where(pair => userIds.Contains(pair.Item1))
        .ToList();

    private async Task<Guid> SeedUserAsync(
        int sendHour,
        string timeZoneId,
        bool digestEnabled = true,
        string? email = "octocat@example.com",
        bool tokenInvalid = false,
        bool withRepository = true)
    {
        var userBuilder = new UserBuilder();
        if (tokenInvalid)
        {
            userBuilder.WithInvalidToken();
        }

        var user = userBuilder.Build();
        var entities = new List<object>
        {
            user,
            new UserSettingsBuilder().ForUser(user.Id).WithEmail(email).WithSchedule(sendHour, timeZoneId, digestEnabled).Build(),
        };
        if (withRepository)
        {
            entities.Add(new TrackedRepositoryBuilder().ForUser(user.Id).Build());
        }

        await SeedAsync([.. entities]);
        return user.Id;
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
