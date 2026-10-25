using System.Net;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Settings;

namespace DevPulse.Infrastructure.Email;

/// <summary>Treść maila potwierdzającego nowy adres digestu (HTML + tekst).</summary>
public static class EmailConfirmationMessage
{
    public const string Subject = "Potwierdź adres email w DevPulse";

    public static EmailMessage Create(string to, string confirmationLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationLink);

        var hours = (int)EmailConfirmationToken.Lifetime.TotalHours;
        var link = WebUtility.HtmlEncode(confirmationLink);
        var address = WebUtility.HtmlEncode(to);

        var html = $"""
            <!DOCTYPE html>
            <html lang="pl">
            <body style="font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; color: #1f1d24; line-height: 1.5;">
              <h1 style="font-size: 20px;">Potwierdź adres email</h1>
              <p>Ktoś (prawdopodobnie Ty) ustawił adres <strong>{address}</strong> jako adres codziennego digestu DevPulse.</p>
              <p>
                <a href="{link}" style="display: inline-block; padding: 10px 16px; background: #5b3bff; color: #fff; text-decoration: none; border-radius: 6px;">
                  Potwierdź adres
                </a>
              </p>
              <p style="color: #6b6375; font-size: 14px;">Link jest ważny {hours} godziny. Jeśli to nie Ty – zignoruj tę wiadomość, adres nie zostanie zmieniony.</p>
            </body>
            </html>
            """;

        var text = $"""
            Potwierdź adres email

            Ktoś (prawdopodobnie Ty) ustawił adres {to} jako adres codziennego digestu DevPulse.
            Otwórz link, aby potwierdzić: {confirmationLink}

            Link jest ważny {hours} godziny. Jeśli to nie Ty – zignoruj tę wiadomość.
            """;

        return new EmailMessage(to, Subject, html, text);
    }
}
