namespace DevPulse.Infrastructure.Ai;

/// <summary>
/// Sekcja <c>Anthropic</c>. Klucz wyłącznie ze zmiennej środowiskowej <c>ANTHROPIC_API_KEY</c> (DigestModule wpisuje go
/// do <see cref="ApiKey"/>) – nigdy w appsettings i nigdy w logach.
/// </summary>
internal sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5-5";

    /// <summary>Maks. 5 zdań w JSON – 400 tokenów wystarcza z zapasem i ogranicza koszt digestu.</summary>
    public int MaxTokens { get; set; } = 400;

    /// <summary>Krótkie zadanie klasyfikacyjne – niski effort, bez rozszerzonego myślenia.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>Po przekroczeniu digest idzie z priorytetami regułowymi.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com/");

    public string ApiVersion { get; set; } = "2026-10-21";
}
