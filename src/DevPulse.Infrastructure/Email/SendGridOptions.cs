namespace DevPulse.Infrastructure.Email;

/// <summary>Sekcja <c>SendGrid</c>. Klucz API tylko w env / user-secrets (<c>SendGrid__ApiKey</c>), nigdy w appsettings.json.</summary>
internal sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";

    public string? ApiKey { get; set; }

    /// <summary>Adres nadawcy – domena musi być zweryfikowana w SendGrid (Sender Authentication).</summary>
    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = "DevPulse";

    /// <summary>SendGrid waliduje żądanie, ale nie wysyła maila – do testów kontraktowych i środowisk testowych.</summary>
    public bool SandboxMode { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.sendgrid.com/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
