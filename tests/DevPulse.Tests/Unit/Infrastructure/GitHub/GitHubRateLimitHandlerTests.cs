using System.Net;
using System.Net.Http.Headers;
using DevPulse.Application.Errors;
using DevPulse.Infrastructure.GitHub;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Unit.Infrastructure.GitHub;

[Trait("Category", "Unit")]
public class GitHubRateLimitHandlerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);
    private readonly FakeGitHubTokenStore _tokenStore = new();
    private readonly GitHubRateLimitBudget _budget = new();

    [Fact]
    public async Task Should_RejectInteractiveRequestWithRetryAfter_When_BudgetBelowReserve()
    {
        // Arrange – ostatnia odpowiedź: 499 punktów, reset za 10 min
        var resetAt = TestClock.Now.AddMinutes(10);
        var (invoker, inner) = CreateInvoker((_, _) => GitHubResponses.Json("[]").WithRateLimit(499, resetAt));
        await invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Act
        var act = () => invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubRateLimitedException>()).Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
        inner.Requests.Should().HaveCount(1, "żądanie poniżej rezerwy nie trafia do GitHuba");
    }

    [Fact]
    public async Task Should_LetJobRequestThrough_When_BudgetBelowReserve()
    {
        // Arrange – rezerwa istnieje właśnie dla jobów
        var (invoker, inner) = CreateInvoker((_, _) => GitHubResponses.Json("{}").WithRateLimit(10, TestClock.Now.AddMinutes(10)));
        await invoker.SendAsync(Request(interactive: false), CancellationToken.None);

        // Act
        using var response = await invoker.SendAsync(Request(interactive: false), CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Should_AllowInteractiveRequest_When_RateLimitWindowAlreadyReset()
    {
        // Arrange
        var (invoker, inner) = CreateInvoker((_, _) => GitHubResponses.Json("[]").WithRateLimit(5, TestClock.Now.AddMinutes(1)));
        await invoker.SendAsync(Request(interactive: true), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));

        // Act
        using var response = await invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Should_IgnoreGraphQlBudget_When_InteractiveRestRequestSent()
    {
        // Arrange – budżety REST (core) i GraphQL są w GitHubie osobne
        var (invoker, _) = CreateInvoker((_, _) => GitHubResponses.Json("{}").WithRateLimit(1, TestClock.Now.AddMinutes(30), "graphql"));
        await invoker.SendAsync(Request(interactive: false), CancellationToken.None);

        // Act
        using var response = await invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_MarkTokenInvalidAndThrow_When_GitHubReturns401()
    {
        // Arrange
        var (invoker, _) = CreateInvoker((_, _) => GitHubResponses.Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));

        // Act
        var act = () => invoker.SendAsync(Request(interactive: false), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubTokenInvalidException>();
        _tokenStore.InvalidatedUsers.Should().Equal(UserId);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Should_ThrowUnavailable_When_GitHubReturns5xx(HttpStatusCode status)
    {
        // Arrange
        var (invoker, _) = CreateInvoker((_, _) => new HttpResponseMessage(status));

        // Act
        var act = () => invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubUnavailableException>();
    }

    [Fact]
    public async Task Should_ThrowRateLimitedWithRetryAfter_When_SecondaryRateLimitHit()
    {
        // Arrange – secondary limit: 403 z retry-after (to nie brak uprawnień)
        var (invoker, _) = CreateInvoker((_, _) =>
        {
            var response = GitHubResponses.Json("""{"message":"You have exceeded a secondary rate limit"}""", HttpStatusCode.Forbidden);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return response;
        });

        // Act
        var act = () => invoker.SendAsync(Request(interactive: false), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubRateLimitedException>()).Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
        _tokenStore.InvalidatedUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_ThrowRateLimitedUntilReset_When_PrimaryLimitExhausted()
    {
        // Arrange
        var resetAt = TestClock.Now.AddMinutes(20);
        var (invoker, _) = CreateInvoker((_, _) =>
            GitHubResponses.Json("""{"message":"API rate limit exceeded"}""", HttpStatusCode.Forbidden).WithRateLimit(0, resetAt));

        // Act
        var act = () => invoker.SendAsync(Request(interactive: false), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubRateLimitedException>()).Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task Should_ThrowRateLimited_When_GitHubReturns429()
    {
        // Arrange
        var (invoker, _) = CreateInvoker((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        // Act
        var act = () => invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubRateLimitedException>();
    }

    [Fact]
    public async Task Should_ReturnResponse_When_ForbiddenWithoutRateLimitSignals()
    {
        // Arrange – np. SSO organizacji: decyzję (brak dostępu) podejmuje klient
        var (invoker, _) = CreateInvoker((_, _) =>
            GitHubResponses.Json("""{"message":"Resource protected by organization SAML enforcement"}""", HttpStatusCode.Forbidden)
                .WithRateLimit(4000, TestClock.Now.AddMinutes(30)));

        // Act
        using var response = await invoker.SendAsync(Request(interactive: true), CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_Throw_When_RequestHasNoUserId()
    {
        // Arrange
        var (invoker, _) = CreateInvoker((_, _) => GitHubResponses.Json("{}"));

        // Act
        var act = () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private (HttpMessageInvoker Invoker, ScriptedGitHubHandler Inner) CreateInvoker(Func<RecordedRequest, int, HttpResponseMessage> respond)
    {
        var inner = new ScriptedGitHubHandler(respond);
        var handler = new GitHubRateLimitHandler(
            _budget,
            _tokenStore,
            Options.Create(new GitHubOptions { RateLimitReserve = 500 }),
            _timeProvider,
            NullLogger<GitHubRateLimitHandler>.Instance)
        {
            InnerHandler = inner,
        };
        return (new HttpMessageInvoker(handler), inner);
    }

    private static HttpRequestMessage Request(bool interactive)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos");
        request.Options.Set(GitHubRequestOptions.UserId, UserId);
        request.Options.Set(GitHubRequestOptions.Interactive, interactive);
        return request;
    }
}
