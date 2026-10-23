namespace DevPulse.Application.Interfaces;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? PlainTextBody = null);

/// <summary>Wysyłka email (SendGrid). Błąd rzuca wyjątek – job oznacza digest jako Failed, a Hangfire ponawia.</summary>
public interface IEmailSender
{
    /// <returns><c>X-Message-Id</c> dostawcy (korelacja z jego logami), jeśli został zwrócony.</returns>
    Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
