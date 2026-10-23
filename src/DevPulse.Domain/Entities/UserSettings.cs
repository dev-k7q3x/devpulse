using DevPulse.Domain.ValueObjects;

namespace DevPulse.Domain.Entities;

/// <summary>Ustawienia 1:1 z <see cref="User"/> (klucz współdzielony).</summary>
public sealed class UserSettings
{
    public const int EmailMaxLength = 254;
    public const int TimeZoneIdMaxLength = 64;
    public const int TokenHashLength = 32;
    public const string DefaultTimeZoneId = "UTC";

    private UserSettings()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Potwierdzony adres docelowy digestu.</summary>
    public string? Email { get; private set; }

    public bool EmailConfirmed { get; private set; }

    public string? PendingEmail { get; private set; }

    /// <summary>SHA-256 tokenu z linku potwierdzającego. Sam token nie jest przechowywany.</summary>
    public byte[]? PendingEmailTokenHash { get; private set; }

    public DateTimeOffset? PendingEmailTokenExpiresAt { get; private set; }

    public SendHour SendHour { get; private set; } = SendHour.Default;

    public string TimeZoneId { get; private set; } = DefaultTimeZoneId;

    public bool DigestEnabled { get; private set; } = true;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <param name="userId">Id właściciela (klucz współdzielony z <see cref="User"/>).</param>
    /// <param name="verifiedEmail">Primary verified email z GitHuba – już zweryfikowany, więc od razu potwierdzony.</param>
    /// <param name="now">Bieżący czas UTC z TimeProvider.</param>
    public static UserSettings CreateDefault(Guid userId, string? verifiedEmail, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Brak id użytkownika.", nameof(userId));
        }

        var email = string.IsNullOrWhiteSpace(verifiedEmail) || verifiedEmail.Length > EmailMaxLength ? null : verifiedEmail;

        return new UserSettings
        {
            UserId = userId,
            Email = email,
            EmailConfirmed = email is not null,
            UpdatedAt = Guard.Utc(now, nameof(now)),
        };
    }

    public void UpdateSchedule(SendHour sendHour, string timeZoneId, bool digestEnabled, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (timeZoneId.Length > TimeZoneIdMaxLength)
        {
            throw new ArgumentException($"Id strefy ma maksymalnie {TimeZoneIdMaxLength} znaków.", nameof(timeZoneId));
        }

        SendHour = sendHour;
        TimeZoneId = timeZoneId;
        DigestEnabled = digestEnabled;
        UpdatedAt = Guard.Utc(now, nameof(now));
    }

    /// <summary>Nowa zmiana nadpisuje poprzednią i unieważnia stary token. Digest dalej idzie na <see cref="Email"/>.</summary>
    public void StartEmailChange(string email, byte[] tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(tokenHash);
        Guard.Utc(now, nameof(now));

        if (email.Length > EmailMaxLength)
        {
            throw new ArgumentException($"Adres email ma maksymalnie {EmailMaxLength} znaków.", nameof(email));
        }

        if (tokenHash.Length != TokenHashLength)
        {
            throw new ArgumentException($"Hash tokenu musi mieć {TokenHashLength} bajty (SHA-256).", nameof(tokenHash));
        }

        if (Guard.Utc(expiresAt, nameof(expiresAt)) <= now)
        {
            throw new ArgumentException("Czas wygaśnięcia tokenu musi być w przyszłości.", nameof(expiresAt));
        }

        PendingEmail = email;
        PendingEmailTokenHash = tokenHash;
        PendingEmailTokenExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    public void CancelEmailChange(DateTimeOffset now)
    {
        ClearPendingEmail();
        UpdatedAt = Guard.Utc(now, nameof(now));
    }

    /// <returns><c>false</c>, gdy nie ma oczekującej zmiany albo token wygasł – bez rozróżniania przypadków (jeden błąd invalid-token).</returns>
    public bool TryConfirmPendingEmail(DateTimeOffset now)
    {
        Guard.Utc(now, nameof(now));

        if (PendingEmail is null || PendingEmailTokenExpiresAt is null || PendingEmailTokenExpiresAt <= now)
        {
            return false;
        }

        Email = PendingEmail;
        EmailConfirmed = true;
        ClearPendingEmail();
        UpdatedAt = now;
        return true;
    }

    private void ClearPendingEmail()
    {
        PendingEmail = null;
        PendingEmailTokenHash = null;
        PendingEmailTokenExpiresAt = null;
    }
}
