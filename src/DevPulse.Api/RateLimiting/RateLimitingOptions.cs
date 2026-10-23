namespace DevPulse.Api.RateLimiting;

/// <summary>Sekcja <c>RateLimiting</c> – limity polityk z planu 03, sekcja 7.1.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int DefaultPerMinute { get; set; } = 120;

    public int GitHubReadPerMinute { get; set; } = 20;

    public int GitHubRefreshPerMinute { get; set; } = 2;

    public int RepoMutationsPerMinute { get; set; } = 30;

    public int EmailPerHour { get; set; } = 3;

    public int EmailPerDay { get; set; } = 10;

    public int AnonymousPerMinute { get; set; } = 10;

    public int SensitivePerHour { get; set; } = 3;
}
