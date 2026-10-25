using System.Globalization;

namespace DevPulse.Application.Digests;

/// <summary>Odmiana liczebników w tekstach digestu (1 dzień / 2 dni, 1 PR / 3 PR-y / 5 PR-ów).</summary>
public static class PolishText
{
    public static string Days(int days) => days == 1 ? "1 dzień" : Count(days, "dni");

    public static string OpenPullRequests(int count) => Plural(count, "otwarty PR", "otwarte PR-y", "otwartych PR-ów");

    /// <summary>Forma po „z” (dopełniacz): „z 1 repozytorium”, „z 4 repozytoriów”.</summary>
    public static string RepositoriesGenitive(int count) => Count(count, count == 1 ? "repozytorium" : "repozytoriów");

    /// <summary>Formy: 1 → <paramref name="one"/>, 2–4 (bez 12–14) → <paramref name="few"/>, pozostałe → <paramref name="many"/>.</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var lastDigit = Math.Abs(count) % 10;
        var lastTwoDigits = Math.Abs(count) % 100;
        var isFew = lastDigit is >= 2 and <= 4 && lastTwoDigits is < 12 or > 14;
        var form = count == 1 ? one : isFew ? few : many;

        return Count(count, form);
    }

    private static string Count(int count, string noun) => $"{count.ToString(CultureInfo.InvariantCulture)} {noun}";
}
