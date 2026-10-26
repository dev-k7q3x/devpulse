using System.Net;
using System.Net.Http.Json;
using DevPulse.Api.RateLimiting;
using DevPulse.Api.Security;
using DevPulse.Application.Errors;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Integration;

/// <summary>Etap 6: panel /hangfire (polityka Admin), polityki rate limitingu na wszystkich endpointach, nagłówki bezpieczeństwa.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class OperationsTests(DevPulseApiFactory factory)
{
    /// <summary>Plan 03 §7.1 – każda akcja kontrolera aplikacji z oczekiwaną polityką.</summary>
    public static TheoryData<string, string> ExpectedPolicies => new()
    {
        { "GET api/auth/login", RateLimitPolicies.Anonymous },
        { "POST api/auth/logout", RateLimitPolicies.Default },
        { "GET api/auth/me", RateLimitPolicies.Default },
        { "DELETE api/auth/me", RateLimitPolicies.Sensitive },
        { "GET api/dashboard", RateLimitPolicies.Default },
        { "GET api/digests", RateLimitPolicies.Default },
        { "GET api/digests/{id:guid}", RateLimitPolicies.Default },
        { "GET api/digests/{id:guid}/html", RateLimitPolicies.Default },
        { "GET api/repositories/available", RateLimitPolicies.GitHubRead },
        { "GET api/repositories", RateLimitPolicies.Default },
        { "POST api/repositories", RateLimitPolicies.RepoMutations },
        { "DELETE api/repositories/{id:guid}", RateLimitPolicies.RepoMutations },
        { "GET api/settings", RateLimitPolicies.Default },
        { "PUT api/settings", RateLimitPolicies.Default },
        { "POST api/settings/email", RateLimitPolicies.Email },
        { "DELETE api/settings/email/pending", RateLimitPolicies.Default },
        { "POST api/settings/email/confirm", RateLimitPolicies.Anonymous },
    };

    [Fact]
    public async Task Should_Return401ProblemNotRedirect_When_HangfireRequestedWithoutSession()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/hangfire");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    [Fact]
    public async Task Should_Return403Forbidden_When_RegularUserOpensHangfire()
    {
        // Arrange
        using var client = await factory.CreateClientForUserAsync(new UserBuilder().WithLogin("regular-user").Build());

        // Act
        var response = await client.GetAsync("/hangfire");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Forbidden);
    }

    [Fact]
    public async Task Should_ServeDashboardWithoutSpaPolicy_When_AdminOpensHangfire()
    {
        // Arrange – login z Admin:Logins (wielkość liter bez znaczenia)
        using var client = await factory.CreateClientForUserAsync(
            new UserBuilder().WithLogin(DevPulseApiFactory.AdminLogin.ToUpperInvariant()).Build());

        // Act
        var response = await client.GetAsync("/hangfire");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("DevPulse – Hangfire");
        var policies = response.Headers.TryGetValues("Content-Security-Policy", out var values) ? values : [];
        policies.Should().NotContain(SecurityHeadersMiddleware.SpaContentSecurityPolicy, "panel Hangfire używa skryptów inline");
    }

    [Fact]
    public async Task Should_AddNosniffAndReferrerPolicy_When_ApiResponds()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.Contains("Referrer-Policy").Should().BeTrue();
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ExpectedPolicies))]
    public void Should_UseContractRateLimitPolicy_When_EndpointMapped(string endpoint, string expectedPolicy)
    {
        // Arrange
        var actions = ApplicationActions();

        // Act
        var found = actions.TryGetValue(endpoint, out var policy);

        // Assert
        found.Should().BeTrue($"endpoint {endpoint} powinien istnieć");
        policy.Should().Be(expectedPolicy);
    }

    [Fact]
    public void Should_CoverEveryApplicationEndpoint_When_PoliciesReviewed()
    {
        // Arrange – nowy endpoint bez wpisu w ExpectedPolicies ma oblać test (świadomy wybór polityki)
        var expected = ExpectedPolicies.Select(row => (string)row[0]).ToHashSet();

        // Act
        var actual = ApplicationActions().Keys;

        // Assert
        actual.Should().BeEquivalentTo(expected);
    }

    /// <summary>Akcje kontrolerów aplikacji (bez testowego PipelineTestController) → polityka rate limitingu.</summary>
    private Dictionary<string, string?> ApplicationActions() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
                && action.ControllerTypeInfo.Assembly == typeof(Program).Assembly)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Key: $"{method} {e.RoutePattern.RawText}", Policy: e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName)))
            .ToDictionary(x => x.Key, x => x.Policy);
}
