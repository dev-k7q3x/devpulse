using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.GitHub;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Infrastructure.GitHub;

[Trait("Category", "Unit")]
public class GitHubCiStatusMapperTests
{
    [Theory]
    [InlineData("SUCCESS", CiStatus.Success)]
    [InlineData("FAILURE", CiStatus.Failure)]
    [InlineData("ERROR", CiStatus.Failure)]
    [InlineData("PENDING", CiStatus.Pending)]
    [InlineData("EXPECTED", CiStatus.Pending)]
    public void Should_MapRollupState_When_StateKnown(string state, CiStatus expected)
    {
        // Act
        var status = GitHubCiStatusMapper.FromRollupState(state);

        // Assert
        status.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOMETHING_NEW")]
    public void Should_ReturnNone_When_NoRollupOrUnknownState(string? state)
    {
        // Act
        var status = GitHubCiStatusMapper.FromRollupState(state);

        // Assert
        status.Should().Be(CiStatus.None);
    }
}
