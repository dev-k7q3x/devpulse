using DevPulse.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace DevPulse.Tests.Unit.Api;

/// <summary>Middleware na serwerze testowym w pamięci (bez aplikacji, bazy i sieci).</summary>
[Trait("Category", "Unit")]
public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Should_AddSpaContentSecurityPolicy_When_HtmlPageServed()
    {
        // Arrange
        using var host = await StartAsync(context => WriteAsync(context, "text/html; charset=utf-8"));
        using var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/settings");

        // Assert
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle()
            .Which.Should().Be(SecurityHeadersMiddleware.SpaContentSecurityPolicy);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle()
            .Which.Should().Be(SecurityHeadersMiddleware.ReferrerPolicy);
    }

    [Fact]
    public async Task Should_KeepEndpointPolicy_When_ResponseAlreadyHasContentSecurityPolicy()
    {
        // Arrange – podgląd digestu ustawia własne CSP z sandbox
        const string digestPolicy = "sandbox; default-src 'none'";
        using var host = await StartAsync(context =>
        {
            context.Response.Headers.ContentSecurityPolicy = digestPolicy;
            return WriteAsync(context, "text/html");
        });
        using var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/digest-preview");

        // Assert
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Which.Should().Be(digestPolicy);
    }

    [Theory]
    [InlineData("/api/dashboard", "application/json")]
    [InlineData("/hangfire", "text/html")]
    [InlineData("/health", "text/plain")]
    public async Task Should_NotAddSpaPolicy_When_ApiHangfireOrHealthResponse(string path, string contentType)
    {
        // Arrange – panel Hangfire ma skrypty inline, API i health nie są stronami SPA
        using var host = await StartAsync(context => WriteAsync(context, contentType));
        using var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
    }

    private static async Task<IHost> StartAsync(RequestDelegate endpoint)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.UseDevPulseSecurityHeaders();
                    app.Run(endpoint);
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private static Task WriteAsync(HttpContext context, string contentType)
    {
        context.Response.ContentType = contentType;
        return context.Response.WriteAsync("ok");
    }
}
