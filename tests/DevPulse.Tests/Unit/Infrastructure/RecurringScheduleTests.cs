using DevPulse.Infrastructure.Jobs;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Infrastructure;

[Trait("Category", "Unit")]
public class RecurringScheduleTests
{
    [Theory]
    [InlineData(1, "* * * * *")]
    [InlineData(15, "*/15 * * * *")]
    [InlineData(30, "*/30 * * * *")]
    [InlineData(60, "0 */1 * * *")]
    [InlineData(360, "0 */6 * * *")]
    [InlineData(1440, "0 0 * * *")]
    public void Should_ReturnCron_When_IntervalDividesHourOrDay(int minutes, string expected)
    {
        // Act
        var cron = RecurringSchedule.ToCron(TimeSpan.FromMinutes(minutes));

        // Assert
        cron.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(90)]
    [InlineData(2880)]
    public void Should_Throw_When_IntervalCannotBeExpressedAsCron(int minutes)
    {
        // Act
        var act = () => RecurringSchedule.ToCron(TimeSpan.FromMinutes(minutes));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_Throw_When_IntervalHasSeconds()
    {
        // Act
        var act = () => RecurringSchedule.ToCron(TimeSpan.FromSeconds(90));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
