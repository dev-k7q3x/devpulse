namespace DevPulse.Application.Options;

/// <summary>Sekcja <c>App</c> – publiczny adres SPA do linków w mailach (na Railway: <c>App__PublicBaseUrl=https://&lt;domena&gt;</c>).</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}
