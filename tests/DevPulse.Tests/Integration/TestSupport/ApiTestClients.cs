using DevPulse.Api.Auth;
using DevPulse.Domain.Entities;
using DevPulse.Tests.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>Klienci testowi: anonimowy z własnym IP, zalogowany przez prawdziwe cookie sesji z nagłówkiem X-XSRF-TOKEN.</summary>
public static class ApiTestClients
{
    public const string TestClientId = "test-client-id";

    private static int _nextIp;

    /// <summary>Klient OAuth do testów – handler GitHub wymaga go przy starcie, ale testy nigdy nie łączą się z GitHubem.</summary>
    public static IWebHostBuilder UseTestAuthSettings(this IWebHostBuilder builder) => builder
        .UseSetting("Auth:GitHub:ClientId", TestClientId)
        .UseSetting("Auth:GitHub:ClientSecret", "test-client-secret");

    /// <summary>
    /// Bez przekierowań (asercje na 302) i z unikalnym X-Forwarded-For – partycje rate limitera per IP
    /// nie przenoszą się między testami.
    /// </summary>
    public static HttpClient CreateAnonymousClient<TProgram>(this WebApplicationFactory<TProgram> factory)
        where TProgram : class
    {
        // HTTPS jak na produkcji – cookie korelacji OAuth ma zawsze flagę Secure.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var ip = Interlocked.Increment(ref _nextIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}");
        return client;
    }

    /// <summary>Zapisuje użytkownika z domyślnymi ustawieniami i zwraca zalogowanego klienta.</summary>
    public static async Task<HttpClient> CreateClientForUserAsync(this DevPulseApiFactory factory, User user)
    {
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.UserSettings.Add(new UserSettingsBuilder().ForUser(user.Id).Build());
            await db.SaveChangesAsync();
        }

        return await factory.CreateSignedInClientAsync(user.Id);
    }

    /// <summary>Cookie sesji dla dowolnego id (także nieistniejącego w bazie) + nagłówek X-XSRF-TOKEN.</summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(this DevPulseApiFactory factory, Guid userId)
    {
        var client = factory.CreateAnonymousClient();
        await client.SignInAsync(userId);
        return client;
    }

    /// <summary>Loguje klienta jako <paramref name="userId"/> i ustawia nagłówek X-XSRF-TOKEN nowej sesji.</summary>
    public static async Task SignInAsync(this HttpClient client, Guid userId)
    {
        var signIn = await client.GetAsync($"/api/__test/sign-in/{userId}");
        signIn.EnsureSuccessStatusCode();
        SetXsrfHeader(client, signIn);
    }

    /// <summary>Pobiera token z cookie XSRF-TOKEN (wydaje je GET /api/auth/me) i dokłada nagłówek jak httpClient SPA.</summary>
    public static async Task UseXsrfTokenAsync(this HttpClient client)
    {
        SetXsrfHeader(client, await client.GetAsync("/api/auth/me"));
    }

    private static void SetXsrfHeader(HttpClient client, HttpResponseMessage response)
    {
        var token = ReadCookie(response, XsrfToken.CookieName)
            ?? throw new InvalidOperationException("Odpowiedź nie wydała cookie XSRF-TOKEN.");

        client.DefaultRequestHeaders.Remove(XsrfToken.HeaderName);
        client.DefaultRequestHeaders.Add(XsrfToken.HeaderName, token);
    }

    public static string? ReadCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var prefix = $"{name}=";
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(prefix, StringComparison.Ordinal));
        return cookie is null ? null : Uri.UnescapeDataString(cookie[prefix.Length..].Split(';')[0]);
    }
}
