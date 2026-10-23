namespace DevPulse.Domain.ValueObjects;

/// <summary>Pełna godzina lokalna wysyłki digestu (0–23).</summary>
public readonly record struct SendHour
{
    public const int Min = 0;
    public const int Max = 23;

    public static readonly SendHour Default = new(7);

    public SendHour(int value)
    {
        if (value is < Min or > Max)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Godzina wysyłki musi być w zakresie {Min}–{Max}.");
        }

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
}
