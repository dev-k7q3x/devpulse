using System.Globalization;
using System.Net;
using System.Text;
using DevPulse.Application.Digests;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;

namespace DevPulse.Infrastructure.Email;

/// <summary>
/// HTML maila digestu: tabele i style inline (klienci poczty ignorują arkusze CSS), bez silnika szablonów.
/// Każdy tekst z danych (nazwy repo, treść priorytetów od AI) przechodzi przez <see cref="WebUtility.HtmlEncode(string)"/>.
/// Znaki nowej linii zawsze <c>\n</c> – wynik nie zależy od systemu, na którym działa render.
/// </summary>
internal sealed class DigestEmailRenderer : IDigestEmailRenderer
{
    private const string GitHubBaseUrl = "https://github.com/";

    public RenderedDigestEmail Render(DigestEmailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var timeZone = FindTimeZone(model.TimeZoneId);
        return new RenderedDigestEmail(model.Subject, RenderHtml(model, timeZone), RenderPlainText(model, timeZone));
    }

    private static string RenderHtml(DigestEmailModel model, TimeZoneInfo timeZone)
    {
        var html = new StringBuilder();
        Line(html, "<!DOCTYPE html>");
        Line(html, "<html lang=\"pl\">");
        Line(html, "<head>");
        Line(html, "<meta charset=\"utf-8\">");
        Line(html, "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        Line(html, $"<title>{Encode(model.Subject)}</title>");
        Line(html, "</head>");
        Line(html, "<body style=\"margin:0;padding:0;background:#f4f4f7;color:#1f1d24;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;\">");
        Line(html, "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f4f7;\">");
        Line(html, "<tr><td align=\"center\" style=\"padding:24px 12px;\">");
        Line(html, "<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;background:#ffffff;border-radius:8px;\">");

        Line(html, "<tr><td style=\"padding:24px 24px 8px;\">");
        Line(html, "<p style=\"margin:0;font-size:14px;font-weight:700;color:#5b3bff;\">DevPulse</p>");
        Line(html, $"<h1 style=\"margin:4px 0 0;font-size:20px;\">Digest z dnia {FormatDate(model.DigestDate)}</h1>");
        Line(html, "</td></tr>");

        Line(html, "<tr><td style=\"padding:16px 24px 8px;\">");
        Line(html, "<h2 style=\"margin:0 0 12px;font-size:16px;\">Co wymaga uwagi dziś</h2>");
        if (model.Priorities.Count == 0)
        {
            Line(html, "<p style=\"margin:0;font-size:14px;\">Wszystko w porządku – żadne repozytorium nie wymaga dziś uwagi.</p>");
        }
        else
        {
            Line(html, "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
            foreach (var priority in model.Priorities)
            {
                var (label, color) = Severity(priority.Severity);
                Line(html, "<tr><td style=\"padding:0 0 12px;font-size:14px;line-height:1.5;\">");
                Line(html, $"<span style=\"display:inline-block;padding:1px 8px;border-radius:10px;background:{color};color:#ffffff;font-size:12px;font-weight:600;\">{label}</span>");
                Line(html, $"<a href=\"{Encode(RepositoryUrl(priority.RepoFullName))}\" style=\"color:#5b3bff;font-weight:600;text-decoration:none;\">{Encode(priority.RepoFullName)}</a>");
                Line(html, $"<br>{Encode(priority.Text)}");
                Line(html, "</td></tr>");
            }

            Line(html, "</table>");
        }

        var source = model.AiUsed ? "priorytety przygotowało AI (Claude)" : "priorytety wyznaczone regułami (AI było niedostępne)";
        Line(html, $"<p style=\"margin:0;font-size:12px;color:#6b6375;\">Źródło: {source}.</p>");
        Line(html, "</td></tr>");

        Line(html, "<tr><td style=\"padding:16px 24px;\">");
        Line(html, "<h2 style=\"margin:0 0 12px;font-size:16px;\">Stan repozytoriów</h2>");
        if (model.RepositorySummary.Count == 0)
        {
            Line(html, "<p style=\"margin:0;font-size:14px;\">Nie śledzisz jeszcze żadnych repozytoriów – dodaj je w ustawieniach DevPulse.</p>");
        }
        else
        {
            Line(html, "<table role=\"presentation\" width=\"100%\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px;\">");
            Line(html, "<tr style=\"background:#f4f4f7;text-align:left;\"><th>Repozytorium</th><th>PR</th><th>Issue</th><th>CI</th><th>Ostatnia aktywność</th></tr>");
            foreach (var repository in model.RepositorySummary)
            {
                Line(html, "<tr style=\"border-top:1px solid #e5e4e7;\">"
                    + $"<td><a href=\"{Encode(RepositoryUrl(repository.RepoFullName))}\" style=\"color:#1f1d24;\">{Encode(repository.RepoFullName)}</a></td>"
                    + $"<td>{Number(repository.OpenPrCount)}</td>"
                    + $"<td>{Number(repository.OpenIssueCount)}</td>"
                    + $"<td>{CiLabel(repository.CiStatus)}</td>"
                    + $"<td>{LastActivity(repository.LastActivityAt, timeZone)}</td>"
                    + "</tr>");
            }

            Line(html, "</table>");
        }

        Line(html, "</td></tr>");

        Line(html, "<tr><td style=\"padding:16px 24px 24px;font-size:12px;color:#6b6375;\">");
        if (SafeDashboardUrl(model.DashboardUrl) is { } dashboardUrl)
        {
            Line(html, $"<p style=\"margin:0 0 8px;\"><a href=\"{Encode(dashboardUrl)}\" style=\"color:#5b3bff;\">Otwórz dashboard DevPulse</a></p>");
        }

        Line(html, "<p style=\"margin:0;\">Dostajesz ten mail, bo masz włączony codzienny digest w DevPulse. Godzinę wysyłki i adres zmienisz w ustawieniach.</p>");
        Line(html, "</td></tr>");

        Line(html, "</table>");
        Line(html, "</td></tr>");
        Line(html, "</table>");
        Line(html, "</body>");
        Line(html, "</html>");
        return html.ToString();
    }

    private static string RenderPlainText(DigestEmailModel model, TimeZoneInfo timeZone)
    {
        var text = new StringBuilder();
        Line(text, $"DevPulse – digest z dnia {FormatDate(model.DigestDate)}");
        Line(text, string.Empty);
        Line(text, "CO WYMAGA UWAGI DZIŚ");
        if (model.Priorities.Count == 0)
        {
            Line(text, "Wszystko w porządku – żadne repozytorium nie wymaga dziś uwagi.");
        }

        foreach (var priority in model.Priorities)
        {
            Line(text, $"- [{Severity(priority.Severity).Label}] {priority.RepoFullName}: {priority.Text}");
        }

        Line(text, model.AiUsed ? "(priorytety przygotowało AI – Claude)" : "(priorytety wyznaczone regułami)");
        Line(text, string.Empty);
        Line(text, "STAN REPOZYTORIÓW");
        if (model.RepositorySummary.Count == 0)
        {
            Line(text, "Nie śledzisz jeszcze żadnych repozytoriów.");
        }

        foreach (var repository in model.RepositorySummary)
        {
            Line(text, $"- {repository.RepoFullName}: PR {Number(repository.OpenPrCount)}, issue {Number(repository.OpenIssueCount)}, "
                + $"CI {CiLabel(repository.CiStatus)}, ostatnia aktywność {LastActivity(repository.LastActivityAt, timeZone)}");
        }

        if (SafeDashboardUrl(model.DashboardUrl) is { } dashboardUrl)
        {
            Line(text, string.Empty);
            Line(text, $"Dashboard: {dashboardUrl}");
        }

        return text.ToString();
    }

    private static void Line(StringBuilder builder, string line) => builder.Append(line).Append('\n');

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string LastActivity(DateTimeOffset? lastActivityAt, TimeZoneInfo timeZone) =>
        lastActivityAt is { } value
            ? TimeZoneInfo.ConvertTime(value, timeZone).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : "–";

    /// <summary>Każdy segment osobno – nazwa repo nigdy nie zmieni hosta ani nie wstrzyknie atrybutu.</summary>
    private static string RepositoryUrl(string repoFullName) =>
        GitHubBaseUrl + string.Join('/', repoFullName.Split('/').Select(Uri.EscapeDataString));

    private static string? SafeDashboardUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    private static (string Label, string Color) Severity(PrioritySeverity severity) => severity switch
    {
        PrioritySeverity.High => ("Pilne", "#c62828"),
        PrioritySeverity.Medium => ("Ważne", "#ef6c00"),
        _ => ("Do sprawdzenia", "#6b6375"),
    };

    private static string CiLabel(CiStatus status) => status switch
    {
        CiStatus.Success => "OK",
        CiStatus.Failure => "Błąd",
        CiStatus.Pending => "W toku",
        _ => "Brak",
    };

    private static TimeZoneInfo FindTimeZone(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) ? timeZone : TimeZoneInfo.Utc;
}
