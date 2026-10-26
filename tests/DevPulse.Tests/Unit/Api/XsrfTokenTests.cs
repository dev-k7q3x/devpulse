using DevPulse.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Unit.Api;

[Trait("Category", "Unit")]
public class XsrfTokenTests
{
    [Fact]
    public void Should_IssueReadableSecureCookie_When_HttpsAndPolicyAlways()
    {
        // Arrange – produkcja za proxy (scheme https z X-Forwarded-Proto)
        var (context, antiforgery) = Create(CookieSecurePolicy.Always, isHttps: true);

        // Act
        XsrfToken.Issue(context, antiforgery);

        // Assert
        var cookie = context.Response.Headers.SetCookie.ToString();
        cookie.Should().Contain($"{XsrfToken.CookieName}=").And.Contain("secure");
    }

    [Fact]
    public void Should_SkipTokenWithoutThrowing_When_HttpRequestAndPolicyAlways()
    {
        // Arrange – żądanie po http wprost na port kontenera (bez proxy)
        var (context, antiforgery) = Create(CookieSecurePolicy.Always, isHttps: false);

        // Act
        var act = () => XsrfToken.Issue(context, antiforgery);

        // Assert
        act.Should().NotThrow();
        context.Response.Headers.SetCookie.ToString().Should().NotContain(XsrfToken.CookieName);
    }

    [Fact]
    public void Should_IssueCookieOverHttp_When_PolicySameAsRequest()
    {
        // Arrange – lokalny development po http
        var (context, antiforgery) = Create(CookieSecurePolicy.SameAsRequest, isHttps: false);

        // Act
        XsrfToken.Issue(context, antiforgery);

        // Assert
        var cookie = context.Response.Headers.SetCookie.ToString();
        cookie.Should().Contain($"{XsrfToken.CookieName}=").And.NotContain("secure");
    }

    private static (HttpContext Context, IAntiforgery Antiforgery) Create(CookieSecurePolicy policy, bool isHttps)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddDataProtection().UseEphemeralDataProtectionProvider().Services
            .AddAntiforgery(options =>
            {
                options.HeaderName = XsrfToken.HeaderName;
                options.Cookie.SecurePolicy = policy;
            })
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = isHttps ? "https" : "http";
        return (context, services.GetRequiredService<IAntiforgery>());
    }
}
