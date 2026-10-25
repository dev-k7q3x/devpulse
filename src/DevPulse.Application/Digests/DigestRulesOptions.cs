namespace DevPulse.Application.Digests;

/// <summary>Sekcja <c>Digest</c> – progi reguł fallbacku (gdy AI nie odpowie albo zwróci śmieci).</summary>
public sealed class DigestRulesOptions
{
    public const string SectionName = "Digest";

    /// <summary>Otwarty PR starszy niż próg to priorytet „stary PR”.</summary>
    public TimeSpan StalePullRequestAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Repo bez aktywności dłużej niż próg to priorytet „brak aktywności”.</summary>
    public TimeSpan InactiveAfter { get; set; } = TimeSpan.FromDays(30);
}
