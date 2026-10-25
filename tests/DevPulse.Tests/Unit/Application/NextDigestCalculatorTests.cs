using DevPulse.Application.Settings;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class NextDigestCalculatorTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [Fact]
    public void Should_ReturnTodayAtSendHour_When_HourNotYetPassed()
    {
        // Arrange – 2026-10-21 07:00 UTC, strefa UTC, wysyłka o 9
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 7, 0, 0, TimeSpan.Zero));

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 9, TimeZoneInfo.Utc);

        // Assert
        next.Should().Be(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Should_ReturnTomorrow_When_HourAlreadyPassedOrNow()
    {
        // Arrange – dokładnie o godzinie wysyłki: ta już trwa, następna jutro
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 9, TimeZoneInfo.Utc);

        // Assert
        next.Should().Be(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Should_UseLocalOffset_When_TimeZoneIsWarsawInSummerTime()
    {
        // Arrange – 2026-07-01 04:00 UTC = 06:00 CEST (UTC+2)
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 4, 0, 0, TimeSpan.Zero));

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 7, Warsaw);

        // Assert – 07:00 CEST = 05:00 UTC
        next.Should().Be(new DateTimeOffset(2026, 7, 1, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Should_ShiftByOneHourInUtc_When_ClocksGoBackOnLastSundayOfOctober()
    {
        // Arrange – sobota 2026-10-24 05:30 UTC (07:30 CEST); w niedzielę 25.10 o 03:00 zegary cofają się na 02:00
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 5, 30, 0, TimeSpan.Zero));

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 7, Warsaw);

        // Assert – niedziela 07:00 CET (UTC+1) = 06:00 UTC, a nie 05:00 jak w sobotę
        next.Should().Be(new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Should_UseFirstOccurrence_When_SendHourIsRepeatedOnLastSundayOfOctober()
    {
        // Arrange – 02:00–03:00 w nocy 25.10.2026 występuje dwa razy (CEST, potem CET)
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 2, Warsaw);

        // Assert – pierwsze 02:00 (CEST, UTC+2) = 00:00 UTC; jeden digest, nie dwa
        next.Should().Be(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Should_MoveToFirstInstantAfterGap_When_SendHourDoesNotExistOnLastSundayOfMarch()
    {
        // Arrange – 28.03.2027 o 02:00 zegary przeskakują na 03:00, godzina 2 nie istnieje
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 3, 27, 22, 0, 0, TimeSpan.Zero));

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 2, Warsaw);

        // Assert – 03:00 CEST = 01:00 UTC (digest nie przepada)
        next.Should().Be(new DateTimeOffset(2027, 3, 28, 1, 0, 0, TimeSpan.Zero));
        TimeZoneInfo.ConvertTime(next, Warsaw).Hour.Should().Be(3);
    }

    [Fact]
    public void Should_SkipLocalDate_When_DigestAlreadyCreatedThatDay()
    {
        // Arrange – dzisiejszy digest już powstał, zmiana godziny na później nie da drugiego
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 7, 0, 0, TimeSpan.Zero));
        var today = new DateOnly(2026, 10, 21);

        // Act
        var next = NextDigestCalculator.Next(clock.GetUtcNow(), 20, TimeZoneInfo.Utc, date => date == today);

        // Assert
        next.Should().Be(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Should_Throw_When_SendHourOutOfRange(int sendHour)
    {
        // Act
        var act = () => NextDigestCalculator.Next(TestClockNow(), sendHour, TimeZoneInfo.Utc);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static DateTimeOffset TestClockNow() => new FakeTimeProvider(TestClock.Now).GetUtcNow();
}
