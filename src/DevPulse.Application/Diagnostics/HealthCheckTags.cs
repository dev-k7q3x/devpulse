namespace DevPulse.Application.Diagnostics;

/// <summary>Tagi health checków – kontrakt między rejestracją (Infrastructure) a mapowaniem endpointów (Api).</summary>
public static class HealthCheckTags
{
    /// <summary>Checki zależności (PostgreSQL, później Hangfire) wystawiane przez /health/ready.</summary>
    public const string Ready = "ready";
}
