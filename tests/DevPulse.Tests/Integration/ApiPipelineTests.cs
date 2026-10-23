using System.Net;
using System.Net.Http.Json;
using DevPulse.Api.RateLimiting;
using DevPulse.Application.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Integration;

/// <summary>Wspólny potok Api: walidacja FluentValidation, mapowanie błędów na slugi z tabeli 0.1, rate limiting.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class ApiPipelineTests(DevPulseApiFactory factory)
{
    [Fact]
    public async Task Should_Return400ValidationWithContractFieldName_When_RequestInvalid()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/__test/validated", new { githubRepoId = 0 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.Validation);
        problem.Errors.Should().ContainKey("githubRepoId");
    }

    [Fact]
    public async Task Should_ReachAction_When_RequestValid()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/__test/validated", new { githubRepoId = 1296269 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    [InlineData("limit-exceeded", HttpStatusCode.UnprocessableEntity)]
    [InlineData("invalid-token", HttpStatusCode.BadRequest)]
    [InlineData("github-token-invalid", HttpStatusCode.Unauthorized)]
    [InlineData("github-unavailable", HttpStatusCode.BadGateway)]
    [InlineData("github-rate-limited", HttpStatusCode.ServiceUnavailable)]
    public async Task Should_MapExceptionToContractStatusAndSlug_When_ApplicationThrows(string kind, HttpStatusCode expectedStatus)
    {
        // Arrange – slug w odpowiedzi jest taki sam jak nazwa przypadku
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/__test/throw/{kind}");

        // Assert
        response.StatusCode.Should().Be(expectedStatus);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(kind);
        problem.Status.Should().Be((int)expectedStatus);
    }

    [Fact]
    public async Task Should_SetRetryAfterRoundedUp_When_GitHubRateLimited()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/__test/throw/github-rate-limited");

        // Assert
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(91));
    }

    [Fact]
    public async Task Should_Return500WithoutExceptionDetails_When_UnexpectedErrorOccurs()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/__test/throw/unexpected");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("sekret-nie-moze-wyciec");
    }

    [Theory]
    [InlineData("/api/__test/validated", RateLimitPolicies.Default)]
    [InlineData("/api/__test/anonymous-limited", RateLimitPolicies.Anonymous)]
    public void Should_KeepActionPolicyAndDefaultOthers_When_EndpointsBuilt(string route, string expectedPolicy)
    {
        // Arrange – polityka domyślna nie może nadpisać [EnableRateLimiting] na akcji
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        // Act
        var endpoint = endpoints.Single(e => "/" + e.RoutePattern.RawText == route);

        // Assert
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName.Should().Be(expectedPolicy);
    }

    [Fact]
    public async Task Should_Return429RateLimitedWithRetryAfter_When_AnonymousLimitExceeded()
    {
        // Arrange – polityka anonymous: 10 żądań na minutę per IP
        using var client = factory.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync("/api/__test/anonymous-limited")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Act
        var response = await client.GetAsync("/api/__test/anonymous-limited");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.RateLimited);
    }
}
