namespace DevPulse.Domain;

internal static class Guard
{
    /// <summary>Npgsql zapisuje timestamptz wyłącznie z offsetem 0 – łapiemy błąd przy źródle, nie przy SaveChanges.</summary>
    public static DateTimeOffset Utc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Czas musi być w UTC (offset 0). Użyj TimeProvider.GetUtcNow().", paramName);
        }

        return value;
    }

    public static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
