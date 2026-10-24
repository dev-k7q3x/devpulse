using DevPulse.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Api;

[Trait("Category", "Unit")]
public class AuthServiceCollectionExtensionsTests
{
    [Fact]
    public void Should_AlwaysMarkCookiesSecure_When_NotDevelopment()
    {
        // Arrange – za proxy Railway flaga Secure nie może zależeć od X-Forwarded-Proto
        var services = new ServiceCollection();

        // Act
        services.AddDevPulseAuthentication(OAuthConfiguration(), Environment(Environments.Production));
        using var provider = services.BuildServiceProvider();

        // Assert
        SessionCookie(provider).SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    [Fact]
    public void Should_FollowRequestScheme_When_Development()
    {
        // Arrange – lokalny development działa po http
        var services = new ServiceCollection();

        // Act
        services.AddDevPulseAuthentication(OAuthConfiguration(), Environment(Environments.Development));
        using var provider = services.BuildServiceProvider();

        // Assert
        SessionCookie(provider).SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest);
    }

    [Fact]
    public void Should_FailFast_When_GitHubOAuthClientMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services.AddDevPulseAuthentication(configuration, Environment(Environments.Development));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Auth__GitHub__ClientId*");
    }

    private static CookieBuilder SessionCookie(IServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie;

    private static IConfiguration OAuthConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:GitHub:ClientId"] = "client-id",
            ["Auth:GitHub:ClientSecret"] = "client-secret",
        })
        .Build();

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };
}
