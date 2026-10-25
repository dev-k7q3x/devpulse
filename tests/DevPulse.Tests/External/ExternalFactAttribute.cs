namespace DevPulse.Tests.External;

/// <summary>
/// Test z prawdziwym zewnętrznym API (płatny, wymaga kluczy). Pomijany, dopóki nie ustawisz
/// <c>DEVPULSE_RUN_EXTERNAL=1</c> – sam trait <c>External</c> nie chroni przed pełnym <c>dotnet test</c> bez filtra.
/// </summary>
public sealed class ExternalFactAttribute : FactAttribute
{
    public const string EnableVariable = "DEVPULSE_RUN_EXTERNAL";

    public ExternalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Skip = $"Test z zewnętrznym API – uruchom z {EnableVariable}=1 i filtrem Category=External.";
        }
    }
}
