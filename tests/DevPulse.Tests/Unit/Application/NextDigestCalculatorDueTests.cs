using DevPulse.Application.Settings;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application;

/// <summary><see cref="NextDigestCalculator.DueLocalDate"/> – reguła DigestSchedulerJob (okno 1 h, uruchomienie co godzinę).</summary>
[Trait("Category", "Unit")]
public class NextDigestCalculatorDueTests
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Should_ReturnLocalDate_When_SchedulerRunsAtSendHour()
    {
        // Arrange – 2026-10-21 07:00 UTC, strefa UTC, wysyłka o 7
        var now = new DateTimeOffset(2026, 10, 21, 7, 0, 0, TimeSpan.Zero);

        // Act
        var due = NextDigestCalculator.DueLocalDate(now, 7, TimeZoneInfo.Utc, Window);

        // Assert
        due.Should().Be(new DateOnly(2026, 10, 21));
    }

    [Fact]
    public void Should_StillBeDue_When_SchedulerRunsSlightlyLate()
    {
        // Arrange – Hangfire uruchomił job kilka sekund po pełnej godzinie
        var now = new DateTimeOffset(2026, 10, 21, 7, 0, 30, TimeSpan.Zero);

        // Act
        var due = NextDigestCalculator.DueLocalDate(now, 7, TimeZoneInfo.Utc, Window);

        // Assert
        due.Should().Be(new DateOnly(2026, 10, 21));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void Should_ReturnNull_When_SendHourOutsideWindow(int sendHour)
    {
        // Arrange – uruchomienie o 07:00: termin 06:00 obsłużyło poprzednie, 08:00 obsłuży następne
        var now = new DateTimeOffset(2026, 10, 21, 7, 0, 0, TimeSpan.Zero);

        // Act
        var due = NextDigestCalculator.DueLocalDate(now, sendHour, TimeZoneInfo.Utc, Window);

        // Assert
        due.Should().BeNull();
    }

    [Fact]
    public void Should_UseLocalHourAndDate_When_UserInDifferentTimeZones()
    {
        // Arrange – 2026-10-21 05:00 UTC = 07:00 CEST w Warszawie = 01:00 EDT w Nowym Jorku
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);

        // Act
        var warsaw = NextDigestCalculator.DueLocalDate(now, 7, Warsaw, Window);
        var newYork = NextDigestCalculator.DueLocalDate(now, 1, NewYork, Window);
        var newYorkAtSeven = NextDigestCalculator.DueLocalDate(now, 7, NewYork, Window);

        // Assert
        warsaw.Should().Be(new DateOnly(2026, 10, 21));
        newYork.Should().Be(new DateOnly(2026, 10, 21));
        newYorkAtSeven.Should().BeNull();
    }

    [Fact]
    public void Should_ReturnPreviousLocalDate_When_LocalMidnightNotYetReached()
    {
        // Arrange – 2026-10-21 03:00 UTC = 2026-10-20 23:00 EDT
        var now = new DateTimeOffset(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);

        // Act
        var due = NextDigestCalculator.DueLocalDate(now, 23, NewYork, Window);

        // Assert
        due.Should().Be(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public void Should_BeDueOnlyOnce_When_RepeatedHourOnLastSundayOfOctober()
    {
        // Arrange – 2026-10-25 w Warszawie 02:00 występuje dwa razy: 00:00 UTC (CEST) i 01:00 UTC (CET)
        var firstOccurrence = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        var secondOccurrence = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        // Act
        var dueFirst = NextDigestCalculator.DueLocalDate(firstOccurrence, 2, Warsaw, Window);
        var dueSecond = NextDigestCalculator.DueLocalDate(secondOccurrence, 2, Warsaw, Window);

        // Assert
        dueFirst.Should().Be(new DateOnly(2026, 10, 25));
        dueSecond.Should().BeNull();
    }

    [Fact]
    public void Should_BeDueRightAfterGap_When_SendHourDoesNotExistInSpring()
    {
        // Arrange – 2027-03-28 w Warszawie nie ma 02:00 (02:00 CET → 03:00 CEST = 01:00 UTC)
        var now = new DateTimeOffset(2027, 3, 28, 1, 0, 0, TimeSpan.Zero);

        // Act
        var due = NextDigestCalculator.DueLocalDate(now, 2, Warsaw, Window);

        // Assert
        due.Should().Be(new DateOnly(2027, 3, 28));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24)]
    public void Should_Throw_When_WindowInvalid(int hours)
    {
        // Act
        var act = () => NextDigestCalculator.DueLocalDate(DateTimeOffset.UnixEpoch, 7, TimeZoneInfo.Utc, TimeSpan.FromHours(hours));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
