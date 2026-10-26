using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DevPulse.Application.Errors;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace DevPulse.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class HealthEndpointsTests(DevPulseApiFactory factory)
{
    [Fact]
    public async Task Should_Return200Healthy_When_LivenessRequested()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Should_Return200Healthy_When_DatabaseAndHangfireServerAreReady()
    {
        // Arrange – serwer Hangfire rejestruje pierwszy heartbeat chwilę po starcie hosta
        using var client = factory.CreateClient();
        var waited = Stopwatch.StartNew();
        HttpResponseMessage response;

        // Act
        do
        {
            response = await client.GetAsync("/health/ready");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        while (waited.Elapsed < TimeSpan.FromSeconds(30));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Should_Return404ProblemDetails_When_UnknownApiRouteRequested()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/does-not-exist");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Should_ReturnContractSlugInsteadOfRfcLink_When_FrameworkGeneratesProblem()
    {
        // Arrange – brakujący plik: anonimowe 404 ProblemDetails, nie przekierowanie na /login (bez podążania za 302)
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/missing-file.js");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }
}

[Trait("Category", "Integration")]
public class HealthEndpointsWithUnreachableDatabaseTests(UnreachableDatabaseApiFactory factory)
    : IClassFixture<UnreachableDatabaseApiFactory>
{
    [Fact]
    public async Task Should_Return503Unhealthy_When_DatabaseIsUnreachable()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Should_Return200_When_LivenessRequestedAndDatabaseIsUnreachable()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
