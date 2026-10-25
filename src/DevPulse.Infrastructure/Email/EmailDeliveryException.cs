using System.Net;

namespace DevPulse.Infrastructure.Email;

/// <summary>
/// SendGrid odrzucił wiadomość. Komunikat zawiera tylko status HTTP – bez adresu odbiorcy i treści odpowiedzi,
/// bo trafia do logów i historii digestu.
/// </summary>
internal sealed class EmailDeliveryException(HttpStatusCode statusCode)
    : Exception($"SendGrid odrzucił wiadomość (HTTP {(int)statusCode}).")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>429 i 5xx – ponowienie (retry Hangfire) ma sens; 4xx to błąd konfiguracji albo danych.</summary>
    public bool IsTransient => StatusCode == HttpStatusCode.TooManyRequests || (int)StatusCode >= 500;
}
