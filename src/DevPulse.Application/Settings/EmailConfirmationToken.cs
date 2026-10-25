using System.Buffers.Text;
using System.Security.Cryptography;

namespace DevPulse.Application.Settings;

/// <summary>
/// Token z linku potwierdzającego email: 32 losowe bajty (base64url, 43 znaki). W bazie tylko SHA-256 bajtów tokenu –
/// wysoka entropia, więc szybki hash bez soli wystarcza (plan 02 §4.2). Sam token nigdy nie trafia do bazy ani logów.
/// </summary>
public static class EmailConfirmationToken
{
    public const int ByteLength = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static (string Token, byte[] Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <returns><c>null</c>, gdy token nie jest poprawnym base64url 32 bajtów.</returns>
    public static byte[]? TryHash(string? token)
    {
        if (string.IsNullOrEmpty(token) || !Base64Url.IsValid(token, out var decodedLength) || decodedLength != ByteLength)
        {
            return null;
        }

        return SHA256.HashData(Base64Url.DecodeFromChars(token));
    }
}
