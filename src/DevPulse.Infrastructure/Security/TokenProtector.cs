using System.Security.Cryptography;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace DevPulse.Infrastructure.Security;

/// <summary>
/// Data Protection (AES-256-CBC + HMAC-SHA256) z własnym purpose – odseparowane kryptograficznie od cookie sesji.
/// Payload zawiera id klucza, więc rotacja pierścienia nie psuje starych tokenów.
/// </summary>
internal sealed class TokenProtector(IDataProtectionProvider dataProtectionProvider) : ITokenProtector
{
    public const string Purpose = "DevPulse.GitHubAccessToken.v1";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(string accessToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        return _protector.Protect(accessToken);
    }

    public string Unprotect(string protectedAccessToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedAccessToken);

        try
        {
            return _protector.Unprotect(protectedAccessToken);
        }
        catch (FormatException exception)
        {
            // Payload spoza base64url – dla wołającego to ten sam przypadek co podmieniony ciphertext.
            throw new CryptographicException("Payload tokenu jest uszkodzony.", exception);
        }
    }
}
