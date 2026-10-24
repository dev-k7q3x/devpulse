using System.Net;
using System.Net.Http.Json;
using System.Web;
using AspNet.Security.OAuth.GitHub;
using DevPulse.Api.Auth;
using DevPulse.Application.DTOs.Auth;
using DevPulse.Application.Interfaces;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Integration;

/// <summary>
/// Pełny przebieg GitHub OAuth: /api/auth/login → GitHub (pominięty) → /signin-github. Wymiana kodu i profil
/// idą przez <see cref="FakeGitHubOAuthHandler"/> z fixture'ami – żadnych żądań do prawdziwego GitHuba.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class GitHubOAuthFlowTests(DevPulseApiFactory factory)
{
    private const long FixtureGitHubId = 987654321;
    private const string FixtureAccessToken = "gho_fixtureAccessToken000000000000000000";

    [Fact]
    public async Task Should_RedirectToGitHubWithRequiredScopesAndCallback_When_LoginRequested()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/auth/login?returnUrl=%2Fsettings");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be("https://github.com/login/oauth/authorize");
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].Should().Be(ApiTestClients.TestClientId);
        query["scope"]!.Split(' ').Should().BeEquivalentTo("read:user", "user:email", "repo");
        query["redirect_uri"].Should().EndWith("/signin-github");
        query["state"].Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task Should_Return400Validation_When_ReturnUrlNotLocal(string returnUrl)
    {
        // Arrange – ochrona przed open redirect
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Type.Should().Be("validation");
        problem.Errors.Should().ContainKey("returnUrl");
    }

    [Fact]
    public async Task Should_CreateAccountStartSessionAndRedirectToReturnUrl_When_CallbackSucceeds()
    {
        // Arrange
        var gitHub = new FakeGitHubOAuthHandler();
        await using var app = WithFakeGitHub(gitHub);
        using var client = app.CreateAnonymousClient();
        var state = await StartLoginAsync(client, "/settings");

        // Act
        var callback = await client.GetAsync($"/signin-github?code=fixture-code&state={Uri.EscapeDataString(state)}");

        // Assert – przekierowanie i cookie sesji
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/settings");
        ApiTestClients.ReadCookie(callback, AuthServiceCollectionExtensions.SessionCookieName).Should().NotBeNullOrEmpty();
        gitHub.LastTokenRequestBody.Should().Contain("code=fixture-code");

        // Assert – sesja działa
        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        me!.Login.Should().Be("fixture-octocat");
        me.AvatarUrl.Should().Be("https://avatars.githubusercontent.com/u/987654321?v=4");
        me.GitHubTokenValid.Should().BeTrue();
        me.MissingScopes.Should().BeEmpty();

        // Assert – konto w bazie: zaszyfrowany token, primary email jako potwierdzony adres digestu
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.GitHubId == FixtureGitHubId);
        user.Id.Should().Be(me.Id);
        user.AccessTokenEncrypted.Should().NotContain(FixtureAccessToken);
        app.Services.GetRequiredService<ITokenProtector>().Unprotect(user.AccessTokenEncrypted).Should().Be(FixtureAccessToken);
        var settings = await db.UserSettings.SingleAsync(s => s.UserId == user.Id);
        settings.Email.Should().Be("fixture-octocat@example.com");
        settings.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_RedirectToLoginWithAccessDenied_When_UserDeniesOnGitHub()
    {
        // Arrange
        var gitHub = new FakeGitHubOAuthHandler();
        await using var app = WithFakeGitHub(gitHub);
        using var client = app.CreateAnonymousClient();
        var state = await StartLoginAsync(client, "/");

        // Act
        var callback = await client.GetAsync($"/signin-github?error=access_denied&state={Uri.EscapeDataString(state)}");

        // Assert
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/login?error=access_denied");
        gitHub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_RedirectToLoginWithFailure_When_CallbackStateInvalid()
    {
        // Arrange – np. podrobiony callback bez cookie korelacji
        using var client = factory.CreateAnonymousClient();

        // Act
        var callback = await client.GetAsync("/signin-github?code=fixture-code&state=forged");

        // Assert
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.Should().Be("/login?error=login_failed");
    }

    private WebApplicationFactory<Program> WithFakeGitHub(FakeGitHubOAuthHandler handler) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<GitHubAuthenticationOptions>(
                GitHubAuthenticationDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(handler))));

    /// <summary>Challenge ustawia cookie korelacji w kliencie i zwraca <c>state</c>, który GitHub odesłałby w callbacku.</summary>
    private static async Task<string> StartLoginAsync(HttpClient client, string returnUrl)
    {
        var login = await client.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return HttpUtility.ParseQueryString(login.Headers.Location!.Query)["state"]!;
    }
}
