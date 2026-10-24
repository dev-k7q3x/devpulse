using System.Net;
using System.Net.Http.Json;
using DevPulse.Api.Auth;
using DevPulse.Application.DTOs.Auth;
using DevPulse.Application.Errors;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevPulse.Tests.Integration;

/// <summary>/api/auth/me, logout i usunięcie konta: 401 zamiast 302, antiforgery, kaskada i izolacja użytkowników.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class AuthEndpointsTests(DevPulseApiFactory factory)
{
    [Fact]
    public async Task Should_Return401Unauthenticated_When_MeRequestedWithoutSession()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert – 401 ProblemDetails, nie 302 na stronę logowania
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    [Fact]
    public async Task Should_IssueReadableXsrfCookie_When_MeRequestedAnonymously()
    {
        // Arrange – anonimowe POST-y (np. potwierdzenie emaila) też potrzebują tokenu
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        setCookies.Should().Contain(c => c.StartsWith(XsrfToken.CookieName + "=", StringComparison.Ordinal)
            && !c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        setCookies.Should().Contain(c => c.StartsWith(XsrfToken.AntiforgeryCookieName + "=", StringComparison.Ordinal)
            && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_Return401OnProtectedEndpoint_When_NoSession()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.PostAsync("/api/auth/logout", content: null);

        // Assert – brak sesji wygrywa z brakiem tokenu XSRF
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    [Fact]
    public async Task Should_ReturnCurrentUser_When_SessionValid()
    {
        // Arrange
        var user = new UserBuilder().WithLogin("regular-user").WithTokenScopes("repo,user:email").Build();
        using var client = await factory.CreateClientForUserAsync(user);

        // Act
        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        // Assert
        me.Should().BeEquivalentTo(new CurrentUserResponse(
            user.Id,
            "regular-user",
            "https://avatars.githubusercontent.com/u/1",
            GitHubTokenValid: true,
            MissingScopes: ["read:user"],
            IsAdmin: false));
    }

    [Fact]
    public async Task Should_UseContractFieldNames_When_MeSerialized()
    {
        // Arrange
        var user = new UserBuilder().WithLogin(DevPulseApiFactory.AdminLogin).WithInvalidToken().Build();
        using var client = await factory.CreateClientForUserAsync(user);

        // Act
        var json = await client.GetStringAsync("/api/auth/me");

        // Assert
        json.Should().Contain("\"githubTokenValid\":false")
            .And.Contain("\"missingScopes\":[]")
            .And.Contain("\"isAdmin\":true")
            .And.NotContain("accessToken", "token GitHub nigdy nie trafia do DTO");
    }

    [Fact]
    public async Task Should_ReturnOwnProfileOnly_When_TwoUsersSignedIn()
    {
        // Arrange
        var userA = new UserBuilder().WithLogin("user-a").Build();
        var userB = new UserBuilder().WithLogin("user-b").Build();
        using var clientA = await factory.CreateClientForUserAsync(userA);
        using var clientB = await factory.CreateClientForUserAsync(userB);

        // Act
        var meA = await clientA.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        var meB = await clientB.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        // Assert
        meA!.Id.Should().Be(userA.Id);
        meB!.Id.Should().Be(userB.Id);
    }

    [Fact]
    public async Task Should_Return401AndClearSession_When_SessionUserNoLongerExists()
    {
        // Arrange – cookie z id konta, którego nie ma w bazie (np. usunięte w innej sesji)
        using var client = await factory.CreateSignedInClientAsync(Guid.CreateVersion7());

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Set-Cookie")
            .Should().Contain(c => c.StartsWith(AuthServiceCollectionExtensions.SessionCookieName + "=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Return400_When_PostWithoutXsrfHeader()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await factory.CreateClientForUserAsync(user);
        client.DefaultRequestHeaders.Remove(XsrfToken.HeaderName);

        // Act
        var response = await client.PostAsync("/api/auth/logout", content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Validation);
    }

    [Fact]
    public async Task Should_Return400_When_XsrfTokenBelongsToAnotherSession()
    {
        // Arrange – token wydany anonimowo nie pasuje do sesji użytkownika
        var user = new UserBuilder().Build();
        using var anonymous = factory.CreateAnonymousClient();
        var anonymousToken = ApiTestClients.ReadCookie(await anonymous.GetAsync("/api/auth/me"), XsrfToken.CookieName)!;
        using var client = await factory.CreateClientForUserAsync(user);
        client.DefaultRequestHeaders.Remove(XsrfToken.HeaderName);
        client.DefaultRequestHeaders.Add(XsrfToken.HeaderName, anonymousToken);

        // Act
        var response = await client.PostAsync("/api/auth/logout", content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_EndSession_When_LogoutWithXsrfHeader()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await factory.CreateClientForUserAsync(user);

        // Act
        var response = await client.PostAsync("/api/auth/logout", content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_DeleteAccountCascadeAndEndSession_When_DeleteMe()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var other = new UserBuilder().Build();
        using var client = await factory.CreateClientForUserAsync(user);
        using var otherClient = await factory.CreateClientForUserAsync(other);
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).Build();
        await using (var seed = factory.CreateDbContext())
        {
            seed.AddRange(
                repository,
                new RepositorySnapshotBuilder().ForRepository(repository.Id).Build(),
                new DigestBuilder().ForUser(user.Id).Build());
            await seed.SaveChangesAsync();
        }

        // Act
        var response = await client.DeleteAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var db = factory.CreateDbContext();
        (await db.Users.AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
        (await db.UserSettings.AnyAsync(s => s.UserId == user.Id)).Should().BeFalse();
        (await db.TrackedRepositories.AnyAsync(r => r.UserId == user.Id)).Should().BeFalse();
        (await db.RepositorySnapshots.AnyAsync(s => s.TrackedRepositoryId == repository.Id)).Should().BeFalse();
        (await db.Digests.AnyAsync(d => d.UserId == user.Id)).Should().BeFalse();
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Assert – konto innego użytkownika nietknięte
        (await otherClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Return429RateLimited_When_SensitiveLimitExceeded()
    {
        // Arrange – polityka sensitive: 3 żądania na godzinę per userId. DELETE kończy sesję, więc przed kolejnym
        // logujemy to samo id ponownie (konta nie ma w bazie – usunięcie jest idempotentne).
        var userId = Guid.CreateVersion7();
        using var client = await factory.CreateSignedInClientAsync(userId);
        for (var i = 0; i < 3; i++)
        {
            (await client.DeleteAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            await client.SignInAsync(userId);
        }

        // Act
        var response = await client.DeleteAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.RateLimited);
    }
}
