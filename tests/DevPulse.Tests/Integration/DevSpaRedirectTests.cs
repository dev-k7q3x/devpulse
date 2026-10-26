using System.Net;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;

namespace DevPulse.Tests.Integration;

/// <summary>
/// Development bez buildu SPA (jak lokalnie i w testach): strony otwarte na porcie API przekierowują do serwera Vite,
/// a /api zostaje przy 404 ProblemDetails.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class DevSpaRedirectTests(DevPulseApiFactory factory)
{
    [Theory]
    [InlineData("/", "http://localhost:5173/")]
    [InlineData("/settings?tab=email", "http://localhost:5173/settings?tab=email")]
    public async Task Should_RedirectToViteDevServer_When_SpaPageRequestedOnApiPort(string path, string expectedLocation)
    {
        // Arrange – np. powrót z /signin-github, gdy callback OAuth wskazuje port API
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.Should().Be(expectedLocation);
    }

    [Fact]
    public async Task Should_KeepProblemDetails404_When_UnknownApiRouteRequested()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/unknown");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
