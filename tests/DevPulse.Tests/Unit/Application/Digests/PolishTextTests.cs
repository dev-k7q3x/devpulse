using DevPulse.Application.Digests;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application.Digests;

[Trait("Category", "Unit")]
public class PolishTextTests
{
    [Theory]
    [InlineData(1, "1 otwarty PR")]
    [InlineData(2, "2 otwarte PR-y")]
    [InlineData(4, "4 otwarte PR-y")]
    [InlineData(5, "5 otwartych PR-ów")]
    [InlineData(12, "12 otwartych PR-ów")]
    [InlineData(14, "14 otwartych PR-ów")]
    [InlineData(22, "22 otwarte PR-y")]
    [InlineData(0, "0 otwartych PR-ów")]
    public void Should_UsePolishPluralForm_When_CountingPullRequests(int count, string expected)
    {
        // Act
        var text = PolishText.OpenPullRequests(count);

        // Assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 dzień")]
    [InlineData(2, "2 dni")]
    [InlineData(31, "31 dni")]
    public void Should_UseDayOrDays_When_CountingDays(int days, string expected)
    {
        // Act
        var text = PolishText.Days(days);

        // Assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 repozytorium")]
    [InlineData(3, "3 repozytoriów")]
    public void Should_UseGenitive_When_CountingRepositoriesAfterPreposition(int count, string expected)
    {
        // Act
        var text = PolishText.RepositoriesGenitive(count);

        // Assert
        text.Should().Be(expected);
    }
}
