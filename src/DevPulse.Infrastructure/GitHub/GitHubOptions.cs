namespace DevPulse.Infrastructure.GitHub;

/// <summary>Sekcja <c>GitHub</c> – progi i limity klienta GitHub API (wartości domyślne wystarczają; konfiguracja tylko do nadpisania).</summary>
internal sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public Uri ApiBaseUrl { get; set; } = new("https://api.github.com/");

    /// <summary>Timeout pojedynczego żądania – po nim <c>github-unavailable</c>.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Poniżej tylu pozostałych punktów limitu żądania interaktywne dostają 503 – budżet zostaje dla jobów (plan 03 §7.2).</summary>
    public int RateLimitReserve { get; set; } = 500;

    /// <summary>Jak długo lista <c>available</c> leży w cache per użytkownik.</summary>
    public TimeSpan AvailableCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Bezpiecznik paginacji <c>GET /user/repos</c> (100 repo na stronę).</summary>
    public int MaxRepositoryPages { get; set; } = 50;

    /// <summary>Rozmiar paczki <c>nodes(ids:)</c> w GraphQL.</summary>
    public int SnapshotBatchSize { get; set; } = 20;
}
