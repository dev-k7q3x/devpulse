using System.Net;
using System.Net.Http.Headers;
using DevPulse.Application.Errors;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.DependencyInjection;
using DevPulse.Infrastructure.GitHub;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Unit.Infrastructure.GitHub;

[Trait("Category", "Unit")]
public class GitHubClientTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);
    private readonly FakeGitHubTokenStore _tokenStore = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public async Task Should_FollowLinkHeaderAndMapAllPages_When_RepositoryListHasNextPage()
    {
        // Arrange
        var (client, handler) = CreateClient((request, _) => request.Uri.Query.Contains("page=2", StringComparison.Ordinal)
            ? GitHubResponses.FromFixture("user-repos-page2.json")
            : WithNextLink(GitHubResponses.FromFixture("user-repos-page1.json"), "https://api.github.com/user/repos?per_page=100&page=2"));

        // Act
        var repositories = await client.GetUserRepositoriesAsync(UserId, CancellationToken.None);

        // Assert
        repositories.Select(r => r.FullName).Should().Equal("octocat/Hello-World", "acme/devpulse", "acme/zeta-tools");
        var privateRepo = repositories[1];
        privateRepo.Id.Should().Be(2000001);
        privateRepo.NodeId.Should().Be("R_kgDOAcme0001");
        privateRepo.Owner.Should().Be("acme");
        privateRepo.IsPrivate.Should().BeTrue();
        privateRepo.DefaultBranch.Should().Be("develop");
        privateRepo.PushedAt.Should().BeNull();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Uri.PathAndQuery.Should().Contain("affiliation=owner,collaborator,organization_member");
        handler.Requests[0].Authorization.Should().Be($"Bearer {FakeGitHubTokenStore.AccessToken}");
    }

    [Fact]
    public async Task Should_SendIfNoneMatchAndReuseCachedPage_When_GitHubReturns304()
    {
        // Arrange – 304 nie zużywa limitu GitHuba, a dane bierzemy z poprzedniej odpowiedzi
        var (client, handler) = CreateClient((_, call) =>
        {
            if (call == 1)
            {
                var first = GitHubResponses.FromFixture("user-repos-page1.json");
                first.Headers.ETag = new EntityTagHeaderValue("\"etag-v1\"");
                return first;
            }

            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        await client.GetUserRepositoriesAsync(UserId, CancellationToken.None);

        // Act
        var repositories = await client.GetUserRepositoriesAsync(UserId, CancellationToken.None);

        // Assert
        handler.Requests[1].IfNoneMatch.Should().Be("\"etag-v1\"");
        repositories.Should().HaveCount(2);
    }

    [Fact]
    public async Task Should_MapStatesPullRequestsCiAndMissingNode_When_GraphQlReturnsNodes()
    {
        // Arrange
        var nodeIds = new[] { "MDEwOlJlcG9zaXRvcnkxMjk2MjY5", "R_kgDOAcme0001", "R_kgDOGone0001" };
        var (client, handler) = CreateClient((_, _) => GitHubResponses.FromFixture("graphql-repository-states.json"));

        // Act
        var results = await client.GetRepositoryStatesAsync(UserId, nodeIds, CancellationToken.None);

        // Assert
        results.Select(r => r.NodeId).Should().Equal(nodeIds);

        var hello = results[0].State!;
        hello.OpenPrCount.Should().Be(14);
        hello.OpenIssueCount.Should().Be(7, "issues.totalCount z GraphQL nie zawiera PR-ów");
        hello.CiStatus.Should().Be(CiStatus.Success);
        hello.CiCommitSha.Should().Be("7fd1a60b01f91b314f59955a4e4d4e80d8edf11d");
        hello.DefaultBranch.Should().Be("main");
        hello.PushedAt.Should().Be(new DateTimeOffset(2026, 10, 20, 8, 15, 0, TimeSpan.Zero));
        hello.OpenPullRequests.Should().HaveCount(2);
        hello.OpenPullRequests[0].Should().BeEquivalentTo(new
        {
            Number = 42,
            Title = "Add digest scheduler",
            Author = "monalisa",
            IsDraft = false,
        });
        hello.OpenPullRequests[1].Author.Should().BeNull("usunięte konto (ghost) nie ma autora");

        var renamed = results[1].State!;
        renamed.Name.Should().Be("devpulse-renamed");
        renamed.CiStatus.Should().Be(CiStatus.Failure);
        renamed.IsPrivate.Should().BeTrue();

        results[2].State.Should().BeNull();
        results[2].Error.Should().Be("NotFound");

        handler.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/graphql");
        handler.Requests[0].Body.Should().Contain("R_kgDOGone0001");
    }

    [Fact]
    public async Task Should_SendBatchesOfConfiguredSize_When_ManyNodeIdsRequested()
    {
        // Arrange – 45 repo przy paczce 20 → 3 zapytania GraphQL
        var nodeIds = Enumerable.Range(1, 45).Select(i => $"R_node{i}").ToList();
        var (client, handler) = CreateClient((request, _) =>
        {
            var count = request.Body!.Split("R_node").Length - 1;
            var nodes = string.Join(',', Enumerable.Repeat("null", count));
            return GitHubResponses.Json("{\"data\":{\"nodes\":[" + nodes + "]}}");
        });

        // Act
        var results = await client.GetRepositoryStatesAsync(UserId, nodeIds, CancellationToken.None);

        // Assert
        handler.Requests.Should().HaveCount(3);
        results.Should().HaveCount(45).And.OnlyContain(r => r.Error == "NotFound");
    }

    [Fact]
    public async Task Should_ThrowRateLimited_When_GraphQlReportsRateLimitedError()
    {
        // Arrange
        var (client, _) = CreateClient((_, _) => GitHubResponses.FromFixture("graphql-rate-limited.json"));

        // Act
        var act = () => client.GetRepositoryStatesAsync(UserId, ["R_node1"], CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubRateLimitedException>();
    }

    [Fact]
    public async Task Should_ThrowUnavailable_When_ResponseIsNotValidJson()
    {
        // Arrange – np. strona błędu proxy z HTML zamiast JSON
        var (client, _) = CreateClient((_, _) => GitHubResponses.Json("<html>Bad gateway</html>"));

        // Act
        var act = () => client.GetUserRepositoriesAsync(UserId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubUnavailableException>();
    }

    [Fact]
    public async Task Should_ReturnRepository_When_FetchedById()
    {
        // Arrange
        var (client, handler) = CreateClient((_, _) => GitHubResponses.FromFixture("repository.json"));

        // Act
        var repository = await client.GetRepositoryAsync(UserId, 4000001, CancellationToken.None);

        // Assert
        repository!.FullName.Should().Be("octocat/solo");
        repository.DefaultBranch.Should().Be("trunk");
        handler.Requests.Single().Uri.AbsolutePath.Should().Be("/repositories/4000001");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Should_ReturnNull_When_RepositoryMissingOrInaccessible(HttpStatusCode status)
    {
        // Arrange
        var (client, _) = CreateClient((_, _) => GitHubResponses.Json("""{"message":"Not Found"}""", status));

        // Act
        var repository = await client.GetRepositoryAsync(UserId, 999, CancellationToken.None);

        // Assert
        repository.Should().BeNull();
    }

    [Fact]
    public async Task Should_ThrowUnavailable_When_GitHubDoesNotRespondInTime()
    {
        // Arrange
        var options = new GitHubOptions { RequestTimeout = TimeSpan.FromMilliseconds(50) };
        var (client, _) = CreateClient((_, _) => GitHubResponses.FromFixture("repository.json"), options, delay: TimeSpan.FromSeconds(5));

        // Act
        var act = () => client.GetRepositoryAsync(UserId, 4000001, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubUnavailableException>();
    }

    [Fact]
    public async Task Should_NotCallGitHub_When_TokenAlreadyInvalid()
    {
        // Arrange
        _tokenStore.TokenInvalid = true;
        var (client, handler) = CreateClient((_, _) => GitHubResponses.FromFixture("repository.json"));

        // Act
        var act = () => client.GetUserRepositoriesAsync(UserId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubTokenInvalidException>();
        handler.Requests.Should().BeEmpty();
    }

    private (GitHubClient Client, ScriptedGitHubHandler Handler) CreateClient(
        Func<RecordedRequest, int, HttpResponseMessage> respond,
        GitHubOptions? gitHubOptions = null,
        TimeSpan delay = default)
    {
        var options = Options.Create(gitHubOptions ?? new GitHubOptions());
        var scripted = new ScriptedGitHubHandler(respond) { Delay = delay };
        var rateLimitHandler = new GitHubRateLimitHandler(
            new GitHubRateLimitBudget(), _tokenStore, options, _timeProvider, NullLogger<GitHubRateLimitHandler>.Instance)
        {
            InnerHandler = scripted,
        };

        var client = new GitHubClient(
            GitHubModule.CreateHttpClient(rateLimitHandler, options.Value),
            _tokenStore,
            _cache,
            options,
            NullLogger<GitHubClient>.Instance);
        return (client, scripted);
    }

    private static HttpResponseMessage WithNextLink(HttpResponseMessage response, string nextUrl)
    {
        response.Headers.Add("Link", $"<{nextUrl}>; rel=\"next\", <{nextUrl}>; rel=\"last\"");
        return response;
    }
}
