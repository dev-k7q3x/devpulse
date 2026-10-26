namespace DevPulse.Application.Options;

/// <summary>Sekcja <c>Digests</c> – okno historii i retencja (wymaganie: historia z ostatnich 30 dni).</summary>
public sealed class DigestHistoryOptions
{
    public const string SectionName = "Digests";

    /// <summary>
    /// Ile wstecz widać digesty i po jakim czasie <c>RetentionJob</c> je usuwa. Pełne doby – lista filtruje po dacie lokalnej
    /// (<c>digest_date &gt;= dziś − N dni</c>), a retencja po <c>created_at</c>.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);
}
