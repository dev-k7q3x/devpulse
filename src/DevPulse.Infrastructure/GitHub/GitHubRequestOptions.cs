namespace DevPulse.Infrastructure.GitHub;

/// <summary>Kontekst żądania przekazywany z <see cref="GitHubClient"/> do <see cref="GitHubRateLimitHandler"/>.</summary>
internal static class GitHubRequestOptions
{
    /// <summary>Właściciel tokenu – budżet limitu i oznaczanie <c>token_invalid</c> są per użytkownik.</summary>
    public static readonly HttpRequestOptionsKey<Guid> UserId = new("DevPulse.GitHub.UserId");

    /// <summary>Żądanie wywołane przez użytkownika (available, POST repo) – podlega rezerwie budżetu. Joby: <c>false</c>.</summary>
    public static readonly HttpRequestOptionsKey<bool> Interactive = new("DevPulse.GitHub.Interactive");
}
