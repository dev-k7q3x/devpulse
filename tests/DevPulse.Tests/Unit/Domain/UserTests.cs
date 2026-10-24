using DevPulse.Domain.Entities;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class UserTests
{
    [Fact]
    public void Should_SetProfileTokenAndTimestamps_When_Created()
    {
        // Arrange
        var now = TestClock.Now;

        // Act
        var user = User.Create(583231, "octocat", "https://avatars/1", "encrypted", "read:user,repo", now);

        // Assert
        user.Id.Should().NotBeEmpty();
        user.Id.Version.Should().Be(7);
        user.GitHubId.Should().Be(583231);
        user.Login.Should().Be("octocat");
        user.AccessTokenEncrypted.Should().Be("encrypted");
        user.TokenInvalid.Should().BeFalse();
        user.CreatedAt.Should().Be(now);
        user.LastLoginAt.Should().Be(now);
        user.TokenUpdatedAt.Should().Be(now);
    }

    [Fact]
    public void Should_Throw_When_CreatedWithNonUtcTime()
    {
        // Arrange
        var localTime = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.FromHours(2));
        var act = () => User.Create(1, "octocat", null, "encrypted", "repo", localTime);

        // Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Should_Throw_When_LoginLongerThanGitHubLimit()
    {
        // Arrange
        var act = () => User.Create(1, new string('a', User.LoginMaxLength + 1), null, "encrypted", "repo", TestClock.Now);

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_TokenScopesLongerThanLimit()
    {
        // Arrange – obcięta lista scopes dałaby fałszywe missingScopes
        var user = new UserBuilder().Build();
        var tooLongScopes = new string('s', User.TokenScopesMaxLength + 1);

        // Act
        var act = () => user.RecordLogin("octocat", null, "encrypted", tooLongScopes, TestClock.Now);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("tokenScopes");
    }

    [Fact]
    public void Should_ReplaceTokenAndClearInvalidFlag_When_LoginRecorded()
    {
        // Arrange
        var user = new UserBuilder().WithInvalidToken().Build();
        var later = TestClock.Now.AddDays(3);

        // Act
        user.RecordLogin("renamed-octocat", null, "new-encrypted", "read:user,user:email,repo", later);

        // Assert
        user.Login.Should().Be("renamed-octocat");
        user.AccessTokenEncrypted.Should().Be("new-encrypted");
        user.TokenInvalid.Should().BeFalse();
        user.LastLoginAt.Should().Be(later);
        user.CreatedAt.Should().Be(TestClock.Now);
    }

    [Fact]
    public void Should_MarkTokenInvalid_When_GitHubRejectedToken()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var later = TestClock.Now.AddHours(1);

        // Act
        user.MarkTokenInvalid(later);

        // Assert
        user.TokenInvalid.Should().BeTrue();
        user.UpdatedAt.Should().Be(later);
    }
}
