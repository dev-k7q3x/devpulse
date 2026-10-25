using DevPulse.Domain.Entities;

namespace DevPulse.Application.Digests;

/// <summary>
/// Wejście szablonu maila digestu. <see cref="Priorities"/> to wynik AI albo fallback regułowy z <see cref="DigestComposition"/>.
/// </summary>
/// <param name="TimeZoneId">Strefa IANA użytkownika – do wyświetlania dat ostatniej aktywności.</param>
/// <param name="DashboardUrl">Link „Otwórz dashboard” – pomijany, gdy <c>null</c>.</param>
public sealed record DigestEmailModel(
    DateOnly DigestDate,
    string TimeZoneId,
    string Subject,
    IReadOnlyList<DigestPriority> Priorities,
    IReadOnlyList<DigestRepositorySummary> RepositorySummary,
    bool AiUsed,
    string? DashboardUrl);

/// <summary>Gotowy mail – ten sam HTML trafia do historii digestów (<c>digests.html_body</c>).</summary>
public sealed record RenderedDigestEmail(string Subject, string HtmlBody, string PlainTextBody);

/// <summary>Render maila digestu po stronie serwera (bez I/O). Dane z GitHuba są zawsze escapowane.</summary>
public interface IDigestEmailRenderer
{
    RenderedDigestEmail Render(DigestEmailModel model);
}
