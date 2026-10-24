namespace DevPulse.Infrastructure.Security;

/// <summary>
/// Sekcja <c>DataProtection</c>. Klucze leżą w tej samej bazie co zaszyfrowane tokeny, więc na produkcji chroni je
/// certyfikat X.509 z sekretów środowiska – zrzut bazy bez env nie wystarcza do odszyfrowania.
/// </summary>
internal sealed class DataProtectionKeyOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>PFX w base64 (zmienna <c>DataProtection__CertificateBase64</c> na Railway).</summary>
    public string? CertificateBase64 { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>Start bez certyfikatu kończy się błędem. <c>true</c> w appsettings.json, <c>false</c> w Development.</summary>
    public bool RequireCertificate { get; set; }
}
