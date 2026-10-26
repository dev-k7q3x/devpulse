using DevPulse.Domain.Enums;

namespace DevPulse.Application.DigestHistory;

/// <summary>Czysta logika historii digestów: okno widoczności, próg retencji i bezpieczny opis błędu dla użytkownika.</summary>
public static class DigestHistoryRules
{
    /// <summary>Opis błędu, gdy w bazie nie ma znanego kodu (np. surowy komunikat sprzed E5A) – tekstu wyjątku nie pokazujemy.</summary>
    public const string GenericFailureMessage = "Nie udało się wysłać digestu.";

    private static readonly Dictionary<string, string> KnownErrors = new(StringComparer.Ordinal)
    {
        [DigestErrorCodes.EmailDeliveryFailed] = "Dostawca poczty odrzucił wysyłkę. Spróbujemy ponownie.",
        [DigestErrorCodes.EmailNotConfigured] = "Wysyłka maili nie jest skonfigurowana.",
        [DigestErrorCodes.NoRecipient] = "Brak potwierdzonego adresu email.",
        [DigestErrorCodes.ContentFailed] = "Nie udało się przygotować treści digestu.",
    };

    /// <summary>Najstarsza data lokalna widoczna w historii: dziś (w strefie użytkownika) minus pełne doby okna.</summary>
    /// <param name="nowUtc">Bieżący czas z TimeProvider.</param>
    /// <param name="timeZoneId">Strefa IANA z ustawień; nieznana albo pusta → UTC.</param>
    /// <param name="retentionPeriod">Okno historii z konfiguracji (pełne doby).</param>
    public static DateOnly FirstVisibleDate(DateTimeOffset nowUtc, string? timeZoneId, TimeSpan retentionPeriod)
    {
        var days = WholeDays(retentionPeriod);
        var timeZone = !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
            ? found
            : TimeZoneInfo.Utc;
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);

        return localToday.AddDays(-days);
    }

    /// <summary>Digesty utworzone przed tym momentem usuwa <c>RetentionJob</c>.</summary>
    public static DateTimeOffset RetentionCutoff(DateTimeOffset nowUtc, TimeSpan retentionPeriod) =>
        nowUtc - TimeSpan.FromDays(WholeDays(retentionPeriod));

    /// <summary>
    /// Pole <c>error</c> w DTO trafia do użytkownika: tylko dla <see cref="DigestStatus.Failed"/>, tylko opis znanego kodu
    /// z <see cref="DigestErrorCodes"/>, a dla nieznanej treści – ogólny komunikat.
    /// </summary>
    public static string? UserFacingError(DigestStatus status, string? storedError)
    {
        if (status != DigestStatus.Failed)
        {
            return null;
        }

        return storedError is not null && KnownErrors.TryGetValue(storedError, out var message)
            ? message
            : GenericFailureMessage;
    }

    private static int WholeDays(TimeSpan retentionPeriod)
    {
        if (retentionPeriod < TimeSpan.FromDays(1) || retentionPeriod.Ticks % TimeSpan.TicksPerDay != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionPeriod), retentionPeriod, "Okno historii musi być pełną liczbą dób (co najmniej 1).");
        }

        return (int)retentionPeriod.TotalDays;
    }
}
