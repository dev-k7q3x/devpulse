namespace DevPulse.Api.RateLimiting;

/// <summary>Nazwy polityk z planu 03, sekcja 7.1. Kontrolery używają ich w <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>Wszystkie endpointy kontrolerów bez innej polityki – bezpiecznik przed zapętlonym klientem.</summary>
    public const string Default = "default";

    /// <summary><c>GET /repositories/available</c> – chybiony cache to N stron <c>GET /user/repos</c>.</summary>
    public const string GitHubRead = "github-read";

    /// <summary><c>available?refresh=true</c> – pominięcie cache zawsze kosztuje limit GitHuba.</summary>
    public const string GitHubRefresh = "github-refresh";

    /// <summary><c>POST</c>/<c>DELETE /repositories</c>.</summary>
    public const string RepoMutations = "repo-mutations";

    /// <summary><c>POST /settings/email</c> – koszt SendGrid i ochrona przed spamowaniem cudzych adresów.</summary>
    public const string Email = "email";

    /// <summary><c>/auth/login</c>, <c>/settings/email/confirm</c> – per IP.</summary>
    public const string Anonymous = "anonymous";

    /// <summary><c>DELETE /auth/me</c>.</summary>
    public const string Sensitive = "sensitive";
}
