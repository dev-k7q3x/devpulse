using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Domain.Entities;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Integration;

/// <summary>
/// /api/repositories: fake IGitHubClient (bez sieci) i rejestrator jobów zamiast Hangfire. Przypadki błędów z planu 03 §2.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class RepositoriesEndpointsTests(DevPulseApiFactory factory)
{
    private static readonly ConcurrentDictionary<DevPulseApiFactory, RepositoriesApp> Apps = new();

    private RepositoriesApp App => Apps.GetOrAdd(factory, f => new RepositoriesApp(f));

    [Fact]
    public async Task Should_Return401_When_ListRequestedWithoutSession()
    {
        // Arrange
        using var client = App.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/repositories");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    [Fact]
    public async Task Should_FilterPaginateAndMarkTracked_When_AvailableRequested()
    {
        // Arrange
        var user = new UserBuilder().Build();
        App.GitHub.SetRepositories(
            user.Id,
            FakeGitHubClient.Repository(11, "octocat", "hello"),
            FakeGitHubClient.Repository(12, "acme", "zeta", isPrivate: true),
            FakeGitHubClient.Repository(13, "acme", "alpha"));
        using var client = await CreateClientAsync(user, new TrackedRepositoryBuilder().ForUser(user.Id).WithGitHubRepoId(13).WithFullName("acme", "alpha").Build());

        // Act
        var page = await client.GetFromJsonAsync<AvailableRepositoriesResponse>("/api/repositories/available?search=ACME&page=2&pageSize=1");

        // Assert – filtr bez wielkości liter, sortowanie po fullName, paginacja po filtrze
        page!.TotalCount.Should().Be(2);
        page.Page.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            GitHubRepoId = 12L,
            FullName = "acme/zeta",
            IsPrivate = true,
            IsTracked = false,
        });

        var all = await client.GetFromJsonAsync<AvailableRepositoriesResponse>("/api/repositories/available");
        all!.Items.Single(i => i.GitHubRepoId == 13).IsTracked.Should().BeTrue();
    }

    [Fact]
    public async Task Should_ServeFromCacheUntilRefreshRequested_When_AvailableCalledRepeatedly()
    {
        // Arrange
        var user = new UserBuilder().Build();
        App.GitHub.SetRepositories(user.Id, FakeGitHubClient.Repository(21, "octocat", "cached"));
        using var client = await CreateClientAsync(user);

        // Act
        await client.GetAsync("/api/repositories/available");
        await client.GetAsync("/api/repositories/available?page=1&pageSize=10");
        var refreshed = await client.GetAsync("/api/repositories/available?refresh=true");

        // Assert
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        App.GitHub.ListCalls(user.Id).Should().Be(2, "drugie wywołanie z cache, refresh=true pomija cache");
    }

    [Fact]
    public async Task Should_Return429_When_RefreshLimitExceeded()
    {
        // Arrange – github-refresh: 2 żądania na minutę per użytkownik
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user);
        for (var i = 0; i < 2; i++)
        {
            (await client.GetAsync("/api/repositories/available?refresh=true")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Act
        var response = await client.GetAsync("/api/repositories/available?refresh=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/api/repositories/available")).StatusCode.Should().Be(HttpStatusCode.OK, "zwykły odczyt ma osobny limit");
    }

    [Theory]
    [InlineData("invalid-token")]
    [InlineData("rate-limited")]
    [InlineData("unavailable")]
    public async Task Should_MapGitHubErrorsToContract_When_AvailableFails(string failure)
    {
        // Arrange
        var user = new UserBuilder().Build();
        Exception exception = failure switch
        {
            "invalid-token" => new GitHubTokenInvalidException(),
            "rate-limited" => new GitHubRateLimitedException(TimeSpan.FromSeconds(30)),
            _ => new GitHubUnavailableException(),
        };
        App.GitHub.FailWith(user.Id, exception);
        using var client = await CreateClientAsync(user);

        // Act
        var response = await client.GetAsync("/api/repositories/available");

        // Assert
        var (status, type) = failure switch
        {
            "invalid-token" => (HttpStatusCode.Unauthorized, ProblemTypes.GitHubTokenInvalid),
            "rate-limited" => (HttpStatusCode.ServiceUnavailable, ProblemTypes.GitHubRateLimited),
            _ => (HttpStatusCode.BadGateway, ProblemTypes.GitHubUnavailable),
        };
        response.StatusCode.Should().Be(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(type);
    }

    [Fact]
    public async Task Should_Return400Validation_When_PageSizeOutOfRange()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user);

        // Act
        var response = await client.GetAsync("/api/repositories/available?pageSize=101");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("pageSize");
    }

    [Fact]
    public async Task Should_TrackRepositoryWithGitHubMetadataAndScheduleSnapshot_When_Added()
    {
        // Arrange
        var user = new UserBuilder().Build();
        App.GitHub.SetRepositories(user.Id, FakeGitHubClient.Repository(31, "octocat", "tracked", isPrivate: true));
        using var client = await CreateClientAsync(user);

        // Act
        var response = await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = 31 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TrackedRepositoryResponse>();
        created!.FullName.Should().Be("octocat/tracked");
        created.IsPrivate.Should().BeTrue();
        response.Headers.Location!.OriginalString.Should().Be($"/api/repositories/{created.Id}");

        await using var db = factory.CreateDbContext();
        var row = await db.TrackedRepositories.SingleAsync(r => r.Id == created.Id);
        row.UserId.Should().Be(user.Id);
        row.GitHubNodeId.Should().Be("R_node31");

        App.Jobs.Jobs.Should().Contain(j => j.Method.Name == nameof(SnapshotRefreshJob.RefreshRepositoryAsync)
            && (Guid)j.Args[0]! == user.Id
            && (Guid)j.Args[1]! == created.Id);
    }

    [Fact]
    public async Task Should_Return409_When_RepositoryAddedTwice()
    {
        // Arrange
        var user = new UserBuilder().Build();
        App.GitHub.SetRepositories(user.Id, FakeGitHubClient.Repository(41, "octocat", "twice"));
        using var client = await CreateClientAsync(user);
        (await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = 41 })).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var response = await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = 41 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Conflict);
    }

    [Fact]
    public async Task Should_Return422_When_RepositoryLimitReached()
    {
        // Arrange – 50 śledzonych repo (Repositories:MaxPerUser)
        var user = new UserBuilder().Build();
        App.GitHub.SetRepositories(user.Id, FakeGitHubClient.Repository(51, "octocat", "fifty-first"));
        var tracked = Enumerable.Range(0, 50).Select(_ => new TrackedRepositoryBuilder().ForUser(user.Id).Build()).ToArray<object>();
        using var client = await CreateClientAsync(user, tracked);

        // Act
        var response = await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = 51 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.LimitExceeded);
    }

    [Fact]
    public async Task Should_Return404_When_RepositoryNotAccessibleOnGitHub()
    {
        // Arrange – klient nie podsunie repo, do którego nie ma dostępu
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user);

        // Act
        var response = await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = 999_999 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Return400_When_PostWithoutXsrfHeader()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        // Act
        var response = await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ListOnlyOwnRepositoriesSortedByName_When_TwoUsersTrackRepositories()
    {
        // Arrange
        var userA = new UserBuilder().Build();
        var userB = new UserBuilder().Build();
        using var clientA = await CreateClientAsync(
            userA,
            new TrackedRepositoryBuilder().ForUser(userA.Id).WithFullName("zeta", "last").Build(),
            new TrackedRepositoryBuilder().ForUser(userA.Id).WithFullName("acme", "first").Build());
        using var clientB = await CreateClientAsync(userB, new TrackedRepositoryBuilder().ForUser(userB.Id).WithFullName("bravo", "secret").Build());

        // Act
        var listA = await clientA.GetFromJsonAsync<List<TrackedRepositoryResponse>>("/api/repositories");

        // Assert
        listA!.Select(r => r.FullName).Should().Equal("acme/first", "zeta/last");
    }

    [Fact]
    public async Task Should_DeleteRepositoryAndSnapshot_When_OwnRepositoryRemoved()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        using var client = await CreateClientAsync(user, repository, new RepositorySnapshotBuilder().ForRepository(repository.Id).Build());

        // Act
        var response = await client.DeleteAsync($"/api/repositories/{repository.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var db = factory.CreateDbContext();
        (await db.TrackedRepositories.AnyAsync(r => r.Id == repository.Id)).Should().BeFalse();
        (await db.RepositorySnapshots.AnyAsync(s => s.TrackedRepositoryId == repository.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Should_Return404AndKeepRepository_When_RemovingAnotherUsersRepository()
    {
        // Arrange – cudzy zasób to 404, nie 403
        var owner = new UserBuilder().Build();
        var attacker = new UserBuilder().Build();
        var repository = new TrackedRepositoryBuilder().ForUser(owner.Id).Build();
        using var ownerClient = await CreateClientAsync(owner, repository);
        using var attackerClient = await CreateClientAsync(attacker);

        // Act
        var response = await attackerClient.DeleteAsync($"/api/repositories/{repository.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = factory.CreateDbContext();
        (await db.TrackedRepositories.AnyAsync(r => r.Id == repository.Id)).Should().BeTrue();
    }

    /// <summary>Zapisuje użytkownika (z ustawieniami) i encje, zwraca klienta zalogowanego w hoście z fake GitHubem.</summary>
    private async Task<HttpClient> CreateClientAsync(User user, params object[] entities)
    {
        await using (var db = factory.CreateDbContext())
        {
            db.AddRange([user, new UserSettingsBuilder().ForUser(user.Id).Build(), .. entities]);
            await db.SaveChangesAsync();
        }

        var client = App.Factory.CreateAnonymousClient();
        await client.SignInAsync(user.Id);
        return client;
    }

    /// <summary>
    /// Host z podmienionym IGitHubClient i IBackgroundJobClient – tworzony raz na fabrykę bazową (ta sama baza),
    /// zwalniany razem z nią (WebApplicationFactory sprząta fabryki pochodne).
    /// </summary>
    private sealed class RepositoriesApp
    {
        public RepositoriesApp(DevPulseApiFactory baseFactory)
        {
            Factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IGitHubClient>(GitHub);
                services.AddSingleton<IBackgroundJobClient>(Jobs);
            }));
        }

        public FakeGitHubClient GitHub { get; } = new();

        public RecordingBackgroundJobClient Jobs { get; } = new();

        public WebApplicationFactory<Program> Factory { get; }
    }
}
