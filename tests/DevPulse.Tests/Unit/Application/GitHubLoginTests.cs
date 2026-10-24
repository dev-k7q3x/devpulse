using DevPulse.Application.Models.GitHub;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class GitHubLoginTests
{
    [Fact]
    public void Should_OmitAccessTokenAndEmail_When_ConvertedToString()
    {
        // Arrange – rekord przypadkiem przekazany do logu nie może ujawnić tokenu
        var login = new GitHubLogin(42, "octocat", null, "octo@example.com", "gho_secret_token", "repo");

        // Act
        var text = login.ToString();

        // Assert
        text.Should().Contain("octocat").And.Contain("42");
        text.Should().NotContain("gho_secret_token").And.NotContain("octo@example.com");
    }
}
