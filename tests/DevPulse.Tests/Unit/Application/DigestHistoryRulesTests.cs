using DevPulse.Application.DigestHistory;
using DevPulse.Domain.Enums;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class DigestHistoryRulesTests
{
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    [Fact]
    public void Should_ReturnLocalTodayMinusWindow_When_TimeZoneIsUtc()
    {
        // Arrange – TestClock: 2026-10-21 07:00 UTC

        // Act
        var firstVisible = DigestHistoryRules.FirstVisibleDate(TestClock.Now, "UTC", ThirtyDays);

        // Assert
        firstVisible.Should().Be(new DateOnly(2026, 9, 21));
    }

    [Fact]
    public void Should_UseUsersLocalDate_When_TimeZoneIsBehindUtc()
    {
        // Arrange – w Pago Pago (UTC−11) jest jeszcze 2026-10-20 20:00

        // Act
        var firstVisible = DigestHistoryRules.FirstVisibleDate(TestClock.Now, "Pacific/Pago_Pago", ThirtyDays);

        // Assert
        firstVisible.Should().Be(new DateOnly(2026, 9, 20));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Should_FallBackToUtc_When_TimeZoneMissingOrUnknown(string? timeZoneId)
    {
        // Act
        var firstVisible = DigestHistoryRules.FirstVisibleDate(TestClock.Now, timeZoneId, ThirtyDays);

        // Assert
        firstVisible.Should().Be(new DateOnly(2026, 9, 21));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("1.12:00:00")]
    public void Should_Throw_When_WindowIsNotWholeDays(string window)
    {
        // Act
        var act = () => DigestHistoryRules.FirstVisibleDate(TestClock.Now, "UTC", TimeSpan.Parse(window, System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_ReturnNowMinusWindow_When_RetentionCutoffComputed()
    {
        // Act
        var cutoff = DigestHistoryRules.RetentionCutoff(TestClock.Now, ThirtyDays);

        // Assert
        cutoff.Should().Be(TestClock.Now.AddDays(-30));
    }

    [Theory]
    [InlineData(DigestStatus.Sent)]
    [InlineData(DigestStatus.Pending)]
    public void Should_HideError_When_DigestNotFailed(DigestStatus status)
    {
        // Act
        var error = DigestHistoryRules.UserFacingError(status, DigestErrorCodes.EmailDeliveryFailed);

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void Should_DescribeKnownCode_When_DigestFailed()
    {
        // Act
        var error = DigestHistoryRules.UserFacingError(DigestStatus.Failed, DigestErrorCodes.NoRecipient);

        // Assert
        error.Should().Be("Brak potwierdzonego adresu email.");
    }

    [Theory]
    [InlineData("System.Net.Http.HttpRequestException: connection refused at 10.0.0.5")]
    [InlineData(null)]
    public void Should_ReturnGenericMessage_When_StoredErrorUnknown(string? storedError)
    {
        // Arrange – surowy komunikat wyjątku (np. sprzed E5A) nie może trafić do użytkownika

        // Act
        var error = DigestHistoryRules.UserFacingError(DigestStatus.Failed, storedError);

        // Assert
        error.Should().Be(DigestHistoryRules.GenericFailureMessage);
    }
}
