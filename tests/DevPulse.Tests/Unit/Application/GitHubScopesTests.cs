using DevPulse.Application.Models.GitHub;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class GitHubScopesTests
{
    [Fact]
    public void Should_ReturnNoMissingScopes_When_AllRequiredGranted()
    {
        // Arrange
        const string tokenScopes = "repo,read:user,user:email";

        // Act
        var missing = GitHubScopes.Missing(tokenScopes);

        // Assert
        missing.Should().BeEmpty();
    }

    [Fact]
    public void Should_ReturnMissingScopesInRequiredOrder_When_SomeNotGranted()
    {
        // Arrange – użytkownik zawęził scopes na ekranie zgody
        const string tokenScopes = "user:email";

        // Act
        var missing = GitHubScopes.Missing(tokenScopes);

        // Assert
        missing.Should().Equal("read:user", "repo");
    }

    [Fact]
    public void Should_TreatUserScopeAsCoveringUserSubScopes_When_ParentScopeGranted()
    {
        // Arrange
        const string tokenScopes = "user, repo";

        // Act
        var missing = GitHubScopes.Missing(tokenScopes);

        // Assert
        missing.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Should_ReturnAllRequiredScopes_When_TokenScopesEmpty(string? tokenScopes)
    {
        // Act
        var missing = GitHubScopes.Missing(tokenScopes);

        // Assert
        missing.Should().Equal(GitHubScopes.Required);
    }

    [Fact]
    public void Should_NormalizeToCommaSeparatedDistinctList_When_ScopesMixSeparators()
    {
        // Arrange
        const string scopes = " repo user:email,repo ,read:user ";

        // Act
        var normalized = GitHubScopes.Normalize(scopes);

        // Assert
        normalized.Should().Be("repo,user:email,read:user");
    }
}
