using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevPulse.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Email;

/// <summary>
/// SendGrid v3 <c>mail/send</c> przez HttpClient (bez SDK). Sukces = 2xx i nagłówek <c>X-Message-Id</c>;
/// błąd → <see cref="EmailDeliveryException"/> (job oznacza digest jako Failed, Hangfire ponawia).
/// </summary>
internal sealed class SendGridEmailSender(
    HttpClient httpClient,
    IOptions<SendGridOptions> options,
    ILogger<SendGridEmailSender> logger) : IEmailSender
{
    public const string MailSendPath = "v3/mail/send";
    public const string MessageIdHeader = "X-Message-Id";

    private readonly SendGridOptions _options = options.Value;

    public async Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException(
                "Brak konfiguracji SendGrid (zmienne SendGrid__ApiKey i SendGrid__FromEmail).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl, MailSendPath))
        {
            Content = JsonContent.Create(BuildPayload(message)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("SendGrid rejected email with status {StatusCode}", (int)response.StatusCode);
            throw new EmailDeliveryException(response.StatusCode);
        }

        var messageId = response.Headers.TryGetValues(MessageIdHeader, out var values) ? values.FirstOrDefault() : null;
        logger.LogInformation(
            "Email accepted by SendGrid, message id {MessageId}, sandbox {SandboxMode}",
            messageId,
            _options.SandboxMode);

        return messageId;
    }

    private object BuildPayload(EmailMessage message)
    {
        // SendGrid wymaga kolejności: text/plain przed text/html.
        var content = new List<object>();
        if (!string.IsNullOrEmpty(message.PlainTextBody))
        {
            content.Add(new { type = "text/plain", value = message.PlainTextBody });
        }

        content.Add(new { type = "text/html", value = message.HtmlBody });

        return new
        {
            personalizations = new[] { new { to = new[] { new { email = message.To } } } },
            from = new { email = _options.FromEmail, name = _options.FromName },
            subject = message.Subject,
            content,
            mail_settings = new { sandbox_mode = new { enable = _options.SandboxMode } },
        };
    }
}
