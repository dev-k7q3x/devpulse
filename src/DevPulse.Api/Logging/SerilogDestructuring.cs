using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Domain.Entities;
using Serilog;

namespace DevPulse.Api.Logging;

/// <summary>
/// Destrukturyzacja obiektów z danymi wrażliwymi (<c>{@User}</c> itp.) – do logów trafiają tylko pola bezpieczne.
/// Token GitHub (jawny i zaszyfrowany), hash tokenu emaila i treść maili z linkiem potwierdzającym nigdy nie są logowane.
/// </summary>
public static class SerilogDestructuring
{
    public static LoggerConfiguration ApplyDevPulseDestructuring(this LoggerConfiguration configuration) => configuration
        .Destructure.ByTransforming<User>(user => new { user.Id, user.GitHubId, user.Login, user.TokenInvalid })
        .Destructure.ByTransforming<UserSettings>(settings => new
        {
            settings.UserId,
            settings.EmailConfirmed,
            HasPendingEmail = settings.PendingEmail is not null,
            SendHour = settings.SendHour.Value,
            settings.TimeZoneId,
            settings.DigestEnabled,
        })
        .Destructure.ByTransforming<GitHubLogin>(login => new { login.GitHubId, login.Login })
        .Destructure.ByTransforming<EmailMessage>(message => new { message.Subject });
}
