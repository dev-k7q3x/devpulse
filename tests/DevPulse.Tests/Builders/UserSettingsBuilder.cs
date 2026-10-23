using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;

namespace DevPulse.Tests.Builders;

public sealed class UserSettingsBuilder
{
    private Guid _userId = Guid.CreateVersion7();
    private string? _email = "octocat@example.com";
    private SendHour _sendHour = SendHour.Default;
    private string _timeZoneId = UserSettings.DefaultTimeZoneId;
    private bool _digestEnabled = true;
    private (string Email, byte[] TokenHash, DateTimeOffset ExpiresAt)? _pendingEmail;
    private DateTimeOffset _now = TestClock.Now;

    public UserSettingsBuilder ForUser(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public UserSettingsBuilder WithEmail(string? email)
    {
        _email = email;
        return this;
    }

    public UserSettingsBuilder WithSchedule(int sendHour, string timeZoneId, bool digestEnabled = true)
    {
        _sendHour = new SendHour(sendHour);
        _timeZoneId = timeZoneId;
        _digestEnabled = digestEnabled;
        return this;
    }

    public UserSettingsBuilder WithPendingEmail(string email, byte[] tokenHash, DateTimeOffset expiresAt)
    {
        _pendingEmail = (email, tokenHash, expiresAt);
        return this;
    }

    public UserSettingsBuilder At(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    public UserSettings Build()
    {
        var settings = UserSettings.CreateDefault(_userId, _email, _now);
        settings.UpdateSchedule(_sendHour, _timeZoneId, _digestEnabled, _now);

        if (_pendingEmail is { } pending)
        {
            settings.StartEmailChange(pending.Email, pending.TokenHash, pending.ExpiresAt, _now);
        }

        return settings;
    }
}
