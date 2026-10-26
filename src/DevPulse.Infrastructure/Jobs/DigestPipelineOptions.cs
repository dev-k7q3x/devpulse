namespace DevPulse.Infrastructure.Jobs;

/// <summary>Sekcja <c>DigestPipeline</c> – progi DigestSchedulerJob i SendDigestJob. Wartości domyślne w kodzie.</summary>
public sealed class DigestPipelineOptions
{
    public const string SectionName = "DigestPipeline";

    /// <summary>
    /// Termin wysyłki w oknie <c>(teraz − okno, teraz]</c> = digest do wysłania. Równe interwałowi schedulera (co godzinę),
    /// więc każdy termin trafia dokładnie jedno uruchomienie.
    /// </summary>
    public TimeSpan SchedulerWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Wiersz <c>Pending</c> starszy niż ten próg to porzucona próba (np. restart procesu w trakcie wysyłki) –
    /// kolejne uruchomienie może go przejąć. Młodszy = inna instancja joba właśnie wysyła.
    /// Musi być krótszy niż <c>InvisibilityTimeout</c> Hangfire.PostgreSql (domyślnie 30 min): po tym czasie Hangfire
    /// oddaje przerwany job do ponownego wykonania i ten musi już widzieć wiersz jako porzucony. Normalna wysyłka trwa sekundy.
    /// </summary>
    public TimeSpan StalePendingAfter { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// DigestSchedulerJob ponownie enqueue'uje porzucone <c>Pending</c> utworzone w tym oknie – zabezpieczenie na wypadek,
    /// gdyby Hangfire nie oddał przerwanego joba. Starszych nie ruszamy (digest sprzed doby jest już nieaktualny).
    /// </summary>
    public TimeSpan AbandonedRecoveryWindow { get; set; } = TimeSpan.FromDays(1);
}
