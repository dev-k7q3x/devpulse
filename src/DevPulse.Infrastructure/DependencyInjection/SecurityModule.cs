using System.Security.Cryptography.X509Certificates;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Infrastructure.Security;
using DevPulse.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M1 Auth: IAuthService, ITokenProtector (Data Protection, klucze w data_protection_keys), dostęp admina do /hangfire.</summary>
internal static class SecurityModule
{
    /// <summary>Stała nazwa aplikacji – izolacja pierścienia kluczy nie zależy od ścieżki kontenera.</summary>
    public const string DataProtectionApplicationName = "DevPulse";

    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminOptions>().Bind(configuration.GetSection(AdminOptions.SectionName));

        var keyOptions = configuration.GetSection(DataProtectionKeyOptions.SectionName).Get<DataProtectionKeyOptions>()
            ?? new DataProtectionKeyOptions();

        // Kontenery Railway są efemeryczne – klucze w bazie, inaczej restart unieważnia sesje i zaszyfrowane tokeny.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<DevPulseDbContext>();

        if (LoadCertificate(keyOptions) is { } certificate)
        {
            // Certyfikat nie leży w magazynie X509 kontenera, więc do odszyfrowania pierścienia trzeba go podać jawnie.
            dataProtection
                .ProtectKeysWithCertificate(certificate)
                .UnprotectKeysWithAnyCertificate(certificate);
        }
        else if (keyOptions.RequireCertificate)
        {
            throw new InvalidOperationException(
                "Brak certyfikatu Data Protection (zmienna środowiskowa DataProtection__CertificateBase64).");
        }

        services.AddSingleton<ITokenProtector, TokenProtector>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }

    private static X509Certificate2? LoadCertificate(DataProtectionKeyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificateBase64))
        {
            return null;
        }

        // EphemeralKeySet: klucz prywatny tylko w pamięci (bez plików tymczasowych); macOS go nie obsługuje.
        var keyStorageFlags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;

        return X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(options.CertificateBase64),
            options.CertificatePassword,
            keyStorageFlags);
    }
}
