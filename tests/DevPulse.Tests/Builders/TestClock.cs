namespace DevPulse.Tests.Builders;

/// <summary>Stały punkt w czasie (UTC) dla builderów i testów – nigdy DateTime.UtcNow.</summary>
public static class TestClock
{
    public static readonly DateTimeOffset Now = new(2026, 10, 21, 7, 0, 0, TimeSpan.Zero);
}
