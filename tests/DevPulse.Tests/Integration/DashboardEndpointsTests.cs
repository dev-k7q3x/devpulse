using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPulse.Application.DTOs.Dashboard;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>
/// /api/dashboard: wyłącznie dane z bazy (IGitHubClient rzuca przy każdym wywołaniu), stany kart, ETag/304, izolacja.
/// Czas aplikacji z FakeTimeProvider – progi świeżości liczone deterministycznie.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class DashboardEndpointsTests(DevPulseApiFactory factory)
{
    private static readonly ConcurrentDictionary<DevPulseApiFactory, DashboardApp> Apps = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private DashboardApp App => Apps.GetOrAdd(factory, f => new DashboardApp(f));

    [Fact]
    public async Task Should_Return401_When_RequestedWithoutSession()
    {
        // Arrange
        using var client = App.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/dashboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    [Fact]
    public async Task Should_ReturnEmptyDashboard_When_UserTracksNoRepositories()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user);

        // Act
        var dashboard = await GetDashboardAsync(client);

        // Assert
        dashboard.Repositories.Should().BeEmpty();
        dashboard.Summary.Should().Be(new DashboardSummary(0, 0, 0, 0));
        dashboard.OldestRefreshAt.Should().BeNull();
    }

    [Fact]
    public async Task Should_MapEveryCardStateAndSummary_When_SnapshotsInDifferentStates()
    {
        // Arrange – Loading (brak snapshotu), Fresh (10 min), Stale (2 h > próg 1 h), Error (ostatnia próba nieudana)
        var now = App.Time.GetUtcNow();
        var user = new UserBuilder().Build();
        var loading = Repository(user, "acme", "a-loading");
        var fresh = Repository(user, "acme", "b-fresh");
        var stale = Repository(user, "acme", "c-stale");
        var error = Repository(user, "acme", "d-error");
        var sha = new string('f', RepositorySnapshot.CommitShaLength);
        using var client = await CreateClientAsync(
            user,
            loading,
            fresh,
            stale,
            error,
            new RepositorySnapshotBuilder().ForRepository(fresh.Id).WithCounts(3, 7).WithCi(CiStatus.Failure, sha).At(now.AddMinutes(-10)).Build(),
            new RepositorySnapshotBuilder().ForRepository(stale.Id).WithCounts(1, 2).WithCi(CiStatus.Success, sha).At(now.AddHours(-2)).Build(),
            new RepositorySnapshotBuilder().ForRepository(error.Id).WithCounts(4, 0).WithCi(CiStatus.Pending).WithLastError("Forbidden").At(now.AddMinutes(-5)).Build());

        // Act
        var dashboard = await GetDashboardAsync(client);

        // Assert – kolejność po fullName, stany i podsumowanie
        dashboard.Repositories.Select(r => (r.FullName, r.State)).Should().Equal(
            ("acme/a-loading", RepositoryCardState.Loading),
            ("acme/b-fresh", RepositoryCardState.Fresh),
            ("acme/c-stale", RepositoryCardState.Stale),
            ("acme/d-error", RepositoryCardState.Error));
        dashboard.Summary.Should().Be(new DashboardSummary(RepositoryCount: 4, TotalOpenPrs: 8, TotalOpenIssues: 9, FailingCiCount: 1));
        dashboard.OldestRefreshAt.Should().Be(now.AddHours(-2));

        var loadingCard = dashboard.Repositories[0];
        loadingCard.OpenPrCount.Should().BeNull();
        loadingCard.OpenIssueCount.Should().BeNull();
        loadingCard.OpenPullRequests.Should().BeEmpty();
        loadingCard.Ci.Should().Be(new CiInfo(CiStatus.None, null, null));
        loadingCard.RefreshedAt.Should().BeNull();

        var freshCard = dashboard.Repositories[1];
        freshCard.OpenPrCount.Should().Be(3);
        freshCard.Ci.Should().Be(new CiInfo(CiStatus.Failure, sha, $"https://github.com/acme/b-fresh/commit/{sha}/checks"));
        freshCard.OpenPullRequests.Should().HaveCount(2);
        freshCard.OpenPullRequests[0].Should().BeEquivalentTo(new { Number = 42, Author = "octocat", IsDraft = false });

        var errorCard = dashboard.Repositories[3];
        errorCard.LastError.Should().Be("Forbidden");
        errorCard.OpenPrCount.Should().Be(4, "przy błędzie zostają ostatnie znane dane");
        errorCard.Ci.ChecksUrl.Should().BeNull();
    }

    [Fact]
    public async Task Should_Return304_When_IfNoneMatchMatchesCurrentVersion()
    {
        // Arrange
        var now = App.Time.GetUtcNow();
        var user = new UserBuilder().Build();
        var repository = Repository(user, "octocat", "etag");
        using var client = await CreateClientAsync(
            user,
            repository,
            new RepositorySnapshotBuilder().ForRepository(repository.Id).At(now.AddMinutes(-1)).Build());
        var first = await client.GetAsync("/api/dashboard");

        // Act
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(conditional);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag.Should().NotBeNull();
        first.Headers.CacheControl!.Private.Should().BeTrue();
        first.Headers.CacheControl.NoCache.Should().BeTrue();
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Should_ReturnNewVersion_When_RepositoryAddedAfterCachedResponse()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, Repository(user, "octocat", "first"));
        var etag = (await client.GetAsync("/api/dashboard")).Headers.ETag!;
        await SeedAsync(Repository(user, "octocat", "second"));

        // Act
        var response = await GetConditionalAsync(client, etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBe(etag);
        (await response.Content.ReadFromJsonAsync<DashboardResponse>(Json))!.Repositories.Should().HaveCount(2);
    }

    [Fact]
    public async Task Should_ReturnNewVersion_When_SnapshotCrossesStaleThreshold()
    {
        // Arrange – dane w bazie bez zmian, ale karta przechodzi z Fresh do Stale
        var user = new UserBuilder().Build();
        var repository = Repository(user, "octocat", "aging");
        using var client = await CreateClientAsync(
            user,
            repository,
            new RepositorySnapshotBuilder().ForRepository(repository.Id).At(App.Time.GetUtcNow().AddMinutes(-59)).Build());
        var etag = (await client.GetAsync("/api/dashboard")).Headers.ETag!;
        App.Time.Advance(TimeSpan.FromMinutes(2));

        // Act
        var response = await GetConditionalAsync(client, etag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DashboardResponse>(Json))!.Repositories.Single().State
            .Should().Be(RepositoryCardState.Stale);
    }

    [Fact]
    public async Task Should_ShowOnlyOwnCards_When_TwoUsersTrackRepositories()
    {
        // Arrange
        var userA = new UserBuilder().Build();
        var userB = new UserBuilder().Build();
        using var clientA = await CreateClientAsync(userA, Repository(userA, "alice", "private-a"));
        using var clientB = await CreateClientAsync(userB, Repository(userB, "bob", "repo-b"));

        // Act
        var dashboardB = await GetDashboardAsync(clientB);

        // Assert
        dashboardB.Repositories.Select(r => r.FullName).Should().Equal("bob/repo-b");
    }

    [Fact]
    public async Task Should_NeverCallGitHub_When_DashboardRequested()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = Repository(user, "octocat", "no-github");
        using var client = await CreateClientAsync(user, repository);

        // Act
        var response = await client.GetAsync("/api/dashboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        App.GitHub.WasCalledFor(user.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Should_RespondQuickly_When_FiftyRepositoriesWithTenPullRequestsEach()
    {
        // Arrange – limit z planu 03: 50 repo × 10 PR. Cel produkcyjny < 100 ms; próg testu z zapasem na wolne runnery CI.
        var now = App.Time.GetUtcNow();
        var user = new UserBuilder().Build();
        var entities = new List<object>();
        for (var i = 0; i < 50; i++)
        {
            var repository = Repository(user, "load", $"repo-{i:D2}");
            var pullRequests = Enumerable.Range(1, RepositorySnapshot.MaxOpenPullRequests)
                .Select(n => new OpenPullRequest(n, $"PR {n}", $"https://github.com/load/repo-{i:D2}/pull/{n}", "octocat", now.AddDays(-n), false));
            entities.Add(repository);
            entities.Add(new RepositorySnapshotBuilder().ForRepository(repository.Id).WithOpenPullRequests(pullRequests).At(now.AddMinutes(-3)).Build());
        }

        using var client = await CreateClientAsync(user, [.. entities]);
        (await client.GetAsync("/api/dashboard")).EnsureSuccessStatusCode(); // rozgrzewka (JIT, plan zapytania)

        // Act
        var stopwatch = Stopwatch.StartNew();
        var dashboard = await GetDashboardAsync(client);
        stopwatch.Stop();

        // Assert
        dashboard.Repositories.Should().HaveCount(50);
        dashboard.Repositories.Should().OnlyContain(r => r.OpenPullRequests.Count == 10);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750));
    }

    private static TrackedRepository Repository(User user, string owner, string name) =>
        new TrackedRepositoryBuilder().ForUser(user.Id).WithFullName(owner, name).Build();

    private static async Task<DashboardResponse> GetDashboardAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json))!;

    private static async Task<HttpResponseMessage> GetConditionalAsync(HttpClient client, EntityTagHeaderValue etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.IfNoneMatch.Add(etag);
        return await client.SendAsync(request);
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> CreateClientAsync(User user, params object[] entities)
    {
        await SeedAsync([user, new UserSettingsBuilder().ForUser(user.Id).Build(), .. entities]);

        var client = App.Factory.CreateAnonymousClient();
        await client.SignInAsync(user.Id);
        return client;
    }

    /// <summary>Host z IGitHubClient rzucającym przy każdym wywołaniu i FakeTimeProvider – raz na fabrykę bazową (ta sama baza).</summary>
    private sealed class DashboardApp
    {
        public DashboardApp(DevPulseApiFactory baseFactory)
        {
            Factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IGitHubClient>(GitHub);
                services.AddSingleton<TimeProvider>(Time);
            }));
        }

        public ThrowingGitHubClient GitHub { get; } = new();

        public FakeTimeProvider Time { get; } = new(TestClock.Now);

        public WebApplicationFactory<Program> Factory { get; }
    }
}
