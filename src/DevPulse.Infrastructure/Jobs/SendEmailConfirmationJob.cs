using System.Security.Cryptography;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Application.Settings;
using DevPulse.Infrastructure.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// Mail z linkiem potwierdzającym nowy adres. Bez HttpContext – <c>userId</c> jawnie, token zaszyfrowany
/// (argumenty Hangfire są w bazie). Ponowienie po nowszej zmianie adresu niczego nie wysyła: token już nie pasuje.
/// </summary>
public sealed class SendEmailConfirmationJob(
    IDevPulseDbContext dbContext,
    IEmailSender emailSender,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<AppOptions> appOptions,
    TimeProvider timeProvider,
    ILogger<SendEmailConfirmationJob> logger)
{
    public const string TokenProtectorPurpose = "DevPulse.EmailConfirmationToken.v1";

    public async Task ExecuteAsync(Guid userId, string protectedToken, CancellationToken cancellationToken)
    {
        string token;
        try
        {
            token = dataProtectionProvider.CreateProtector(TokenProtectorPurpose).Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            // Np. pierścień kluczy bez klucza z chwili enqueue – ponawianie nic nie da.
            logger.LogWarning("Email confirmation for user {UserId} skipped: token payload cannot be decrypted", userId);
            return;
        }

        var settings = await dbContext.UserSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        var tokenHash = EmailConfirmationToken.TryHash(token);
        if (settings?.PendingEmail is null
            || settings.PendingEmailTokenHash is null
            || tokenHash is null
            || !CryptographicOperations.FixedTimeEquals(settings.PendingEmailTokenHash, tokenHash)
            || settings.PendingEmailTokenExpiresAt is null
            || settings.PendingEmailTokenExpiresAt <= timeProvider.GetUtcNow())
        {
            logger.LogInformation("Email confirmation for user {UserId} skipped: change cancelled, superseded or expired", userId);
            return;
        }

        var link = BuildConfirmationLink(appOptions.Value.PublicBaseUrl, token);
        var messageId = await emailSender.SendAsync(
            EmailConfirmationMessage.Create(settings.PendingEmail, link),
            cancellationToken);

        if (messageId is null)
        {
            logger.LogInformation("Email confirmation handed to sender for user {UserId} without provider message id", userId);
        }
        else
        {
            logger.LogInformation("Email confirmation sent for user {UserId}, provider message id {MessageId}", userId, messageId);
        }
    }

    /// <summary>Link do strony SPA – dopiero ona wysyła POST (skanery linków nie potwierdzą adresu samym GET).</summary>
    public static string BuildConfirmationLink(string publicBaseUrl, string token)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            throw new InvalidOperationException("Brak konfiguracji App:PublicBaseUrl (zmienna App__PublicBaseUrl).");
        }

        return $"{publicBaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(token)}";
    }
}
