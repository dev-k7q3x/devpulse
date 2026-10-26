namespace DevPulse.Application.Settings;

/// <summary>
/// Najbliższa wysyłka digestu: pierwsza pełna godzina <c>sendHour</c> czasu lokalnego strefy po <c>now</c>.
/// Zmiana czasu: nieistniejąca godzina lokalna (wiosną) przesuwa się na pierwszy istniejący moment po luce
/// (Europe/Warsaw, 02:00 → 03:00 CEST); powtórzona godzina (jesienią) liczy się raz – przy pierwszym wystąpieniu.
/// Tę samą regułę musi stosować DigestSchedulerJob (E5A).
/// </summary>
public static class NextDigestCalculator
{
    /// <param name="nowUtc">Bieżący czas z TimeProvider.</param>
    /// <param name="sendHour">Godzina lokalna 0–23.</param>
    /// <param name="timeZone">Strefa użytkownika.</param>
    /// <param name="skipLocalDate">Dni lokalne, w których digest już powstał (UNIQUE user_id + digest_date) – pomijane.</param>
    public static DateTimeOffset Next(
        DateTimeOffset nowUtc,
        int sendHour,
        TimeZoneInfo timeZone,
        Func<DateOnly, bool>? skipLocalDate = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentOutOfRangeException.ThrowIfLessThan(sendHour, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sendHour, 23);

        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);

        // Dwa dni zapasu wystarczą przy jednym pominiętym dniu; pętla dla pewności przy kilku.
        for (var dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            var localDate = localToday.AddDays(dayOffset);
            if (skipLocalDate?.Invoke(localDate) == true)
            {
                continue;
            }

            var candidate = ToUtc(localDate.ToDateTime(new TimeOnly(sendHour, 0)), timeZone);
            if (candidate > nowUtc)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Nie znaleziono terminu wysyłki w ciągu tygodnia.");
    }

    /// <summary>
    /// Czy digest należy się teraz: termin wysyłki (ta sama reguła co <see cref="Next"/>, łącznie ze zmianą czasu)
    /// wypadł w oknie <c>(nowUtc − window, nowUtc]</c>. DigestSchedulerJob uruchamiany co godzinę z oknem 1 h
    /// trafia każdy termin dokładnie raz – godzina powtórzona jesienią nie daje drugiego digestu.
    /// </summary>
    /// <returns>Lokalna data digestu albo <c>null</c>, gdy termin nie wypada w oknie.</returns>
    public static DateOnly? DueLocalDate(DateTimeOffset nowUtc, int sendHour, TimeZoneInfo timeZone, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(window, TimeSpan.FromDays(1));

        var scheduled = Next(nowUtc - window, sendHour, timeZone);
        return scheduled <= nowUtc ? LocalDate(scheduled, timeZone) : null;
    }

    /// <summary>Data lokalna (dzień digestu) dla momentu UTC.</summary>
    public static DateOnly LocalDate(DateTimeOffset utc, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).DateTime);

    private static DateTimeOffset ToUtc(DateTime localTime, TimeZoneInfo timeZone)
    {
        TimeSpan offset;
        if (timeZone.IsInvalidTime(localTime))
        {
            // Luka wiosenna: offset sprzed zmiany daje moment tuż po luce (02:00 +01:00 = 03:00 +02:00).
            offset = timeZone.GetUtcOffset(localTime.AddDays(-1));
        }
        else if (timeZone.IsAmbiguousTime(localTime))
        {
            // Godzina powtórzona: większy offset = wcześniejszy moment UTC = pierwsze wystąpienie.
            offset = timeZone.GetAmbiguousTimeOffsets(localTime).Max();
        }
        else
        {
            offset = timeZone.GetUtcOffset(localTime);
        }

        return new DateTimeOffset(localTime, offset).ToUniversalTime();
    }
}
