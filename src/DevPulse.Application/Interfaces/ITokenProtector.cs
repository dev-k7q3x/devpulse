namespace DevPulse.Application.Interfaces;

/// <summary>
/// Szyfrowanie tokenu GitHub (Data Protection, purpose <c>DevPulse.GitHubAccessToken.v1</c>). Szyfrowanie, nie hash –
/// joby potrzebują tokenu w jawnej postaci. Żadna z wartości nie może trafić do logów.
/// </summary>
public interface ITokenProtector
{
    string Protect(string accessToken);

    /// <summary>Rzuca <see cref="System.Security.Cryptography.CryptographicException"/> dla podmienionego lub obcego payloadu.</summary>
    string Unprotect(string protectedAccessToken);
}
