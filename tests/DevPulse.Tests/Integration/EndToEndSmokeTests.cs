using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPulse.Application.DTOs.Dashboard;
using DevPulse.Application.DTOs.Digests;
using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Integration;

/// <summary>
/// Smoke test etapu 6: cały przepływ na fake'ach – logowanie → wybór repo → snapshot → dashboard → digest → historia.
/// GitHub, Anthropic i SendGrid są podmienione; joby wywoływane wprost (bez serwera Hangfire).
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class EndToEndSmokeTests(DevPulseApiFactory factory)
{
    private const long GitHubRepoId = 777001;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Should_DeliverDigestAndShowItInHistory_When_UserTracksRepository()
    {
        // Arrange – zalogowany użytkownik z potwierdzonym emailem i włączonym digestem
        var gitHub = new SmokeGitHubClient();
        var ai = new FakeAiPrioritizer();
        var email = new RecordingEmailSender();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IGitHubClient>(gitHub);
            services.AddSingleton<IAiPrioritizer>(ai);
            services.AddSingleton<IEmailSender>(email);
            services.AddSingleton<IBackgroundJobClient>(new RecordingBackgroundJobClient());
        }));
        var user = new UserBuilder().WithLogin("smoke-user").Build();
        using var client = await CreateClientForUserAsync(app, user);
        gitHub.Repositories[user.Id] = [FakeGitHubClient.Repository(GitHubRepoId, "octocat", "smoke-repo")];

        // Act 1 – wybór repo z listy GitHuba
        var available = await client.GetFromJsonAsync<AvailableRepositoriesResponse>("/api/repositories/available", Json);
        var added = await client.PostAsJsonAsync("/api/repositories", new { githubRepoId = GitHubRepoId });

        // Assert 1
        available!.Items.Should().ContainSingle(r => r.GitHubRepoId == GitHubRepoId);
        added.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act 2 – snapshot (job), potem dashboard z bazy
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SnapshotRefreshJob>().RefreshUserAsync(user.Id, CancellationToken.None);
        }

        var dashboard = await client.GetFromJsonAsync<DashboardResponse>("/api/dashboard", Json);

        // Assert 2
        var card = dashboard!.Repositories.Should().ContainSingle().Subject;
        card.FullName.Should().Be("octocat/smoke-repo");
        card.State.Should().Be(RepositoryCardState.Fresh);
        card.OpenPrCount.Should().Be(3);
        card.Ci.Status.Should().Be(CiStatus.Failure);

        // Act 3 – digest na dzisiejszą datę (job), potem historia
        var today = DateOnly.FromDateTime(app.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SendDigestJob>().SendAsync(user.Id, today, CancellationToken.None);
        }

        var history = await client.GetFromJsonAsync<List<DigestListItem>>("/api/digests", Json);
        var digest = history.Should().ContainSingle().Subject;
        var preview = await client.GetAsync($"/api/digests/{digest.Id}/html");

        // Assert 3
        email.Sent.Should().ContainSingle().Which.To.Should().Be("octocat@example.com");
        ai.Calls.Should().Be(1);
        digest.Status.Should().Be(DigestStatus.Sent);
        digest.AiUsed.Should().BeTrue();
        digest.Priorities.Should().ContainSingle(p => p.RepoFullName == "octocat/smoke-repo");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        (await preview.Content.ReadAsStringAsync()).Should().Contain("octocat/smoke-repo");
    }

    private async Task<HttpClient> CreateClientForUserAsync(WebApplicationFactory<Program> app, Domain.Entities.User user)
    {
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.UserSettings.Add(new UserSettingsBuilder().ForUser(user.Id).Build());
            await db.SaveChangesAsync();
        }

        var client = app.CreateAnonymousClient();
        await client.SignInAsync(user.Id);
        return client;
    }

    /// <summary>Lista repo i stan z GraphQL (failing CI, 3 PR) – wszystko w pamięci.</summary>
    private sealed class SmokeGitHubClient : IGitHubClient
    {
        public Dictionary<Guid, IReadOnlyList<GitHubRepository>> Repositories { get; } = [];

        public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Repositories.GetValueOrDefault(userId) ?? []);

        public Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken) =>
            Task.FromResult(Repositories.GetValueOrDefault(userId)?.FirstOrDefault(r => r.Id == gitHubRepoId));

        public Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
            Guid userId,
            IReadOnlyList<string> nodeIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<GitHubRepositoryStateResult> results = nodeIds
                .Select(nodeId => new GitHubRepositoryStateResult(nodeId, StateGitHubClient.State(nodeId, "octocat", "smoke-repo"), null))
                .ToList();
            return Task.FromResult(results);
        }
    }
}
