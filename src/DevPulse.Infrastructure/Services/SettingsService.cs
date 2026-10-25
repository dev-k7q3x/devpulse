using DevPulse.Application.DTOs.Settings;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Settings;
using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using DevPulse.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevPulse.Infrastructure.Services;

internal sealed class SettingsService(
    IDevPulseDbContext dbContext,
    TimeProvider timeProvider,
    IBackgroundJobClient backgroundJobs,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<SettingsService> logger) : ISettingsService
{
    public async Task<SettingsResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.UserSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken)
            ?? throw new NotFoundException();

        return await ToResponseAsync(settings, cancellationToken);
    }

    public async Task<SettingsResponse> UpdateAsync(Guid userId, SettingsUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await FindTrackedAsync(userId, cancellationToken);
        settings.UpdateSchedule(new SendHour(request.SendHour), request.TimeZoneId, request.DigestEnabled, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return await ToResponseAsync(settings, cancellationToken);
    }

    public async Task<SettingsResponse> StartEmailChangeAsync(Guid userId, EmailChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await FindTrackedAsync(userId, cancellationToken);
        var email = request.Email.Trim();
        if (settings.EmailConfirmed && string.Equals(settings.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Ten adres jest już aktualnym adresem digestu.");
        }

        // Nowa zmiana nadpisuje poprzednią – stary token przestaje pasować do zapisanego hasha.
        var now = timeProvider.GetUtcNow();
        var (token, tokenHash) = EmailConfirmationToken.Generate();
        settings.StartEmailChange(email, tokenHash, now + EmailConfirmationToken.Lifetime, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Argumenty jobów Hangfire leżą w bazie jawnym tekstem – token przekazujemy zaszyfrowany.
        var protectedToken = dataProtectionProvider
            .CreateProtector(SendEmailConfirmationJob.TokenProtectorPurpose)
            .Protect(token);
        backgroundJobs.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(userId, protectedToken, CancellationToken.None));

        logger.LogInformation("Email change started for user {UserId}, confirmation mail enqueued", userId);

        return await ToResponseAsync(settings, cancellationToken);
    }

    public async Task CancelEmailChangeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await FindTrackedAsync(userId, cancellationToken);
        if (settings.PendingEmail is null)
        {
            return;
        }

        settings.CancelEmailChange(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmailConfirmResponse> ConfirmEmailAsync(EmailConfirmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Zły format, brak wiersza, wygaśnięcie i ponowne użycie – zawsze ten sam błąd (bez enumeracji).
        var tokenHash = EmailConfirmationToken.TryHash(request.Token) ?? throw new InvalidTokenException();
        var settings = await dbContext.UserSettings
            .SingleOrDefaultAsync(s => s.PendingEmailTokenHash == tokenHash, cancellationToken)
            ?? throw new InvalidTokenException();

        if (!settings.TryConfirmPendingEmail(timeProvider.GetUtcNow()))
        {
            throw new InvalidTokenException();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Email confirmed for user {UserId}", settings.UserId);

        return new EmailConfirmResponse(settings.Email ?? string.Empty);
    }

    private async Task<UserSettings> FindTrackedAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserSettings.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken)
            ?? throw new NotFoundException();

    private async Task<SettingsResponse> ToResponseAsync(UserSettings settings, CancellationToken cancellationToken) => new(
        settings.Email,
        settings.EmailConfirmed,
        settings.PendingEmail,
        settings.PendingEmailTokenExpiresAt,
        settings.SendHour.Value,
        settings.TimeZoneId,
        settings.DigestEnabled,
        await NextDigestAtAsync(settings, cancellationToken));

    /// <summary><c>null</c>, gdy digest jest wyłączony albo nie ma potwierdzonego adresu (plan 03 §4).</summary>
    private async Task<DateTimeOffset?> NextDigestAtAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.DigestEnabled || !settings.EmailConfirmed || settings.Email is null
            || !TimeZoneInfo.TryFindSystemTimeZoneById(settings.TimeZoneId, out var timeZone))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var localToday = NextDigestCalculator.LocalDate(now, timeZone);

        // Dzień, w którym digest już powstał, nie dostanie drugiego (UNIQUE user_id + digest_date).
        var digestDates = await dbContext.Digests
            .AsNoTracking()
            .Where(d => d.UserId == settings.UserId && d.DigestDate >= localToday)
            .Select(d => d.DigestDate)
            .ToListAsync(cancellationToken);

        return NextDigestCalculator.Next(now, settings.SendHour.Value, timeZone, digestDates.Contains);
    }
}
