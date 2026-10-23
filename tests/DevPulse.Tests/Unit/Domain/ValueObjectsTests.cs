using DevPulse.Domain.ValueObjects;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class SendHourTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(23)]
    public void Should_CreateSendHour_When_ValueInRange(int value)
    {
        // Arrange – godzina z zakresu 0–23

        // Act
        var sendHour = new SendHour(value);

        // Assert
        sendHour.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Should_Throw_When_ValueOutOfRange(int value)
    {
        // Arrange
        var act = () => new SendHour(value);

        // Act & Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_DefaultToSeven_When_DefaultUsed()
    {
        // Arrange & Act
        var sendHour = SendHour.Default;

        // Assert
        sendHour.Value.Should().Be(7);
        sendHour.ToString().Should().Be("07");
    }
}

[Trait("Category", "Unit")]
public class RepoFullNameTests
{
    [Fact]
    public void Should_SplitOwnerAndName_When_ParsingValidFullName()
    {
        // Arrange
        const string fullName = "octocat/hello-world";

        // Act
        var result = RepoFullName.Parse(fullName);

        // Assert
        result.Owner.Should().Be("octocat");
        result.Name.Should().Be("hello-world");
        result.Value.Should().Be(fullName);
    }

    [Theory]
    [InlineData("no-slash")]
    [InlineData("/name")]
    [InlineData("a/b/c")]
    public void Should_ThrowFormatException_When_ParsingInvalidFullName(string fullName)
    {
        // Arrange
        var act = () => RepoFullName.Parse(fullName);

        // Act & Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Should_Throw_When_OwnerLongerThanGitHubLimit()
    {
        // Arrange
        var act = () => new RepoFullName(new string('a', RepoFullName.OwnerMaxLength + 1), "repo");

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_BeEqual_When_OwnerAndNameAreEqual()
    {
        // Arrange
        var first = new RepoFullName("octocat", "hello-world");
        var second = RepoFullName.Parse("octocat/hello-world");

        // Act
        var equal = first == second;

        // Assert
        equal.Should().BeTrue();
    }
}

[Trait("Category", "Unit")]
public class DigestAiUsageTests
{
    [Fact]
    public void Should_KeepModelAndTokens_When_ValuesValid()
    {
        // Arrange & Act
        var usage = new DigestAiUsage("claude-sonnet-5-5", 900, 150);

        // Assert
        usage.Model.Should().Be("claude-sonnet-5-5");
        usage.InputTokens.Should().Be(900);
        usage.OutputTokens.Should().Be(150);
    }

    [Fact]
    public void Should_Throw_When_TokenCountNegative()
    {
        // Arrange
        var act = () => new DigestAiUsage("claude-sonnet-5-5", -1, 0);

        // Act & Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
