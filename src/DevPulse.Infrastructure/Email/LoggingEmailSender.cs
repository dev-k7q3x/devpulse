using DevPulse.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevPulse.Infrastructure.Email;

/// <summary>
/// Atrapa do czasu podpięcia SendGrid (E3B): niczego nie wysyła, loguje tylko temat i zamaskowanego odbiorcę –
/// treść maila zawiera link z tokenem, więc nie trafia do logów. Rejestrowana przez TryAdd: prawdziwy sender ją zastępuje.
/// Na produkcji rzuca wyjątek – job idzie w retry Hangfire zamiast udawać udaną wysyłkę.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment) : IEmailSender
{
    public Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (environment.IsProduction())
        {
            throw new InvalidOperationException("Brak prawdziwego IEmailSender (SendGrid) – atrapa nie może działać na produkcji.");
        }

        logger.LogWarning(
            "Fake email sender: message {Subject} to {Recipient} was NOT sent (SendGrid not configured)",
            message.Subject,
            MaskRecipient(message.To));

        return Task.FromResult<string?>(null);
    }

    /// <summary><c>jan.kowalski@example.com</c> → <c>j***@example.com</c>.</summary>
    public static string MaskRecipient(string address)
    {
        var at = address.LastIndexOf('@');
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }
}
