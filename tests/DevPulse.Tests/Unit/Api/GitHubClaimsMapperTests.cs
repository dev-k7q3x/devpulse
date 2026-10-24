using System.Security.Claims;
using DevPulse.Api.Auth;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Api;

[Trait("Category", "Unit")]
public class GitHubClaimsMapperTests
{
    private const string AccessToken = "gho_test_token";

    [Fact]
    public void Should_MapProfileClaims_When_GitHubPrincipalComplete()
    {
        // Arrange
        var principal = GitHubPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "583231"),
            new Claim(ClaimTypes.Name, "octocat"),
            new Claim(GitHubClaimsMapper.AvatarUrlClaimType, "https://avatars.githubusercontent.com/u/583231"),
            new Claim(ClaimTypes.Email, "octocat@github.com"));

        // Act
        var login = GitHubClaimsMapper.ToGitHubLogin(principal, AccessToken, "repo,user:email");

        // Assert
        login.GitHubId.Should().Be(583231);
        login.Login.Should().Be("octocat");
        login.AvatarUrl.Should().Be("https://avatars.githubusercontent.com/u/583231");
        login.Email.Should().Be("octocat@github.com");
        login.AccessToken.Should().Be(AccessToken);
        login.TokenScopes.Should().Be("repo,user:email");
    }

    [Fact]
    public void Should_LeaveOptionalFieldsNull_When_AvatarEmailAndScopesMissing()
    {
        // Arrange
        var principal = GitHubPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "octocat"),
            new Claim(ClaimTypes.Email, " "));

        // Act
        var login = GitHubClaimsMapper.ToGitHubLogin(principal, AccessToken, grantedScopes: null);

        // Assert
        login.AvatarUrl.Should().BeNull();
        login.Email.Should().BeNull();
        login.TokenScopes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Should_Throw_When_GitHubIdMissingOrInvalid(string? gitHubId)
    {
        // Arrange
        var claims = new List<Claim> { new(ClaimTypes.Name, "octocat") };
        if (gitHubId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, gitHubId));
        }

        // Act
        var act = () => GitHubClaimsMapper.ToGitHubLogin(GitHubPrincipal([.. claims]), AccessToken, "repo");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Throw_When_LoginMissing()
    {
        // Arrange
        var principal = GitHubPrincipal(new Claim(ClaimTypes.NameIdentifier, "1"));

        // Act
        var act = () => GitHubClaimsMapper.ToGitHubLogin(principal, AccessToken, "repo");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_ContainOnlyDevPulseUserId_When_SessionPrincipalCreated()
    {
        // Arrange – cookie sesji nie niesie danych GitHuba ani tokenu
        var userId = Guid.CreateVersion7();

        // Act
        var principal = GitHubClaimsMapper.CreateSessionPrincipal(userId);

        // Assert
        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.Claims.Should().ContainSingle()
            .Which.Should().Match<Claim>(c => c.Type == DevPulseClaimTypes.UserId && c.Value == userId.ToString());
    }

    [Fact]
    public void Should_Throw_When_SessionPrincipalCreatedForEmptyId()
    {
        // Act
        var act = () => GitHubClaimsMapper.CreateSessionPrincipal(Guid.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static ClaimsPrincipal GitHubPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "GitHub"));
}
