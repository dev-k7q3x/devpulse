namespace DevPulse.Application.DTOs.Settings;

public sealed record SettingsResponse(
    string? Email,
    bool EmailConfirmed,
    string? PendingEmail,
    DateTimeOffset? PendingEmailExpiresAt,
    int SendHour,
    string TimeZoneId,
    bool DigestEnabled,
    DateTimeOffset? NextDigestAt);

/// <summary><c>PUT /api/settings</c>.</summary>
public sealed record SettingsUpdateRequest
{
    public int SendHour { get; init; }

    /// <summary>Strefa IANA, np. <c>Europe/Warsaw</c>.</summary>
    public string TimeZoneId { get; init; } = string.Empty;

    public bool DigestEnabled { get; init; }
}

/// <summary><c>POST /api/settings/email</c> – rozpoczyna zmianę adresu.</summary>
public sealed record EmailChangeRequest
{
    public string Email { get; init; } = string.Empty;
}

/// <summary><c>POST /api/settings/email/confirm</c> – token z linku (base64url, 43 znaki).</summary>
public sealed record EmailConfirmRequest
{
    public string Token { get; init; } = string.Empty;
}

public sealed record EmailConfirmResponse(string Email);
