using DevPulse.Application.Digests;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Email;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

[Trait("Category", "Unit")]
public class DigestEmailRendererTests
{
    private static readonly DateOnly DigestDate = new(2026, 10, 21);

    private readonly DigestEmailRenderer _renderer = new();

    [Fact]
    public void Should_MatchApprovedSnapshot_When_DigestRendered()
    {
        // Arrange – zmiana szablonu wymaga świadomej aktualizacji plików w Fixtures/SendGrid
        var model = SampleModel();

        // Act
        var email = _renderer.Render(model);

        // Assert
        email.Subject.Should().Be(model.Subject);
        email.HtmlBody.Should().Be(Snapshot("digest-email.expected.html"));
        email.PlainTextBody.Should().Be(Snapshot("digest-email.expected.txt"));
    }

    [Fact]
    public void Should_EscapeHtmlInRepositoryNamesAndAiText_When_DataContainsMarkup()
    {
        // Arrange – treść od AI i nazwy z GitHuba traktujemy jak niezaufane dane
        var model = SampleModel() with
        {
            Subject = "<b>temat</b>",
            Priorities = [new DigestPriority("evil/<script>", "Kliknij <a href=\"https://phish.example\">tu</a> & \"teraz\"", PrioritySeverity.High)],
            RepositorySummary = [new DigestRepositorySummary("evil/\"onmouseover=alert(1)", 1, 0, CiStatus.None, null)],
        };

        // Act
        var html = _renderer.Render(model).HtmlBody;

        // Assert
        html.Should().NotContain("<script>").And.NotContain("<a href=\"https://phish.example\">").And.NotContain("<b>temat");
        html.Should().Contain("Kliknij &lt;a href=&quot;https://phish.example&quot;&gt;tu&lt;/a&gt; &amp; &quot;teraz&quot;");
        html.Should().Contain("<title>&lt;b&gt;temat&lt;/b&gt;</title>");
        html.Should().Contain("href=\"https://github.com/evil/%3Cscript%3E\"");
        html.Should().Contain("href=\"https://github.com/evil/%22onmouseover%3Dalert%281%29\"");
    }

    [Fact]
    public void Should_ShowAllClearAndRuleSource_When_NoPrioritiesAndAiUnavailable()
    {
        // Arrange
        var model = SampleModel() with { Priorities = [], AiUsed = false };

        // Act
        var email = _renderer.Render(model);

        // Assert
        email.HtmlBody.Should().Contain("Wszystko w porządku – żadne repozytorium nie wymaga dziś uwagi.");
        email.HtmlBody.Should().Contain("priorytety wyznaczone regułami");
        email.PlainTextBody.Should().Contain("(priorytety wyznaczone regułami)");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData(null)]
    public void Should_OmitDashboardLink_When_UrlNotAbsoluteHttp(string? dashboardUrl)
    {
        // Arrange
        var model = SampleModel() with { DashboardUrl = dashboardUrl };

        // Act
        var email = _renderer.Render(model);

        // Assert
        email.HtmlBody.Should().NotContain("Otwórz dashboard");
        email.PlainTextBody.Should().NotContain("Dashboard:");
    }

    [Fact]
    public void Should_ShowLastActivityInUserTimeZone_When_DateCrossesMidnight()
    {
        // Arrange – 23:30 UTC 20.10 to już 21.10 w Warszawie (UTC+2 w październiku przed zmianą czasu)
        var model = SampleModel() with
        {
            TimeZoneId = "Europe/Warsaw",
            RepositorySummary = [new DigestRepositorySummary("acme/api", 0, 0, CiStatus.Success, new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero))],
        };

        // Act
        var email = _renderer.Render(model);

        // Assert
        email.HtmlBody.Should().Contain("<td>21.10.2026</td>");
    }

    [Fact]
    public void Should_FallBackToUtc_When_TimeZoneUnknown()
    {
        // Arrange
        var model = SampleModel() with
        {
            TimeZoneId = "Mars/Olympus_Mons",
            RepositorySummary = [new DigestRepositorySummary("acme/api", 0, 0, CiStatus.Success, new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero))],
        };

        // Act
        var email = _renderer.Render(model);

        // Assert
        email.HtmlBody.Should().Contain("<td>20.10.2026</td>");
    }

    private static DigestEmailModel SampleModel() => new(
        DigestDate,
        "UTC",
        "DevPulse – 21.10.2026: uwagi wymaga 2 z 3 repozytoriów",
        [
            new DigestPriority("acme/api", "Napraw nieprzechodzące CI na main – blokuje 4 otwarte PR-y.", PrioritySeverity.High),
            new DigestPriority("acme/web", "Przejrzyj PR czekający 12 dni na review.", PrioritySeverity.Medium),
        ],
        [
            new DigestRepositorySummary("acme/api", 4, 7, CiStatus.Failure, TestClock.Now.AddHours(-5)),
            new DigestRepositorySummary("acme/docs", 0, 1, CiStatus.None, null),
            new DigestRepositorySummary("acme/web", 1, 0, CiStatus.Success, TestClock.Now.AddDays(-2)),
        ],
        AiUsed: true,
        DashboardUrl: "https://devpulse.example/");

    private static string Snapshot(string name) =>
        StubHttpMessageHandler.Fixture("SendGrid", name).ReplaceLineEndings("\n");
}
