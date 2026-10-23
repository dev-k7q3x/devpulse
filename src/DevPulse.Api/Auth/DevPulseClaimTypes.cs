namespace DevPulse.Api.Auth;

public static class DevPulseClaimTypes
{
    /// <summary>Id użytkownika DevPulse (uuid) w cookie sesji – partycja rate limitera i źródło ICurrentUser.</summary>
    public const string UserId = "devpulse:user_id";
}
