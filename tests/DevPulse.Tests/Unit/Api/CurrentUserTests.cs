using System.Security.Claims;
using DevPulse.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace DevPulse.Tests.Unit.Api;

[Trait("Category", "Unit")]
public class CurrentUserTests
{
    [Fact]
    public void Should_ReturnUserIdFromSessionClaim_When_Authenticated()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var currentUser = CreateCurrentUser(GitHubClaimsMapper.CreateSessionPrincipal(userId));

        // Act
        var result = currentUser.UserId;

        // Assert
        currentUser.IsAuthenticated.Should().BeTrue();
        result.Should().Be(userId);
    }

    [Fact]
    public void Should_ThrowOnUserId_When_RequestAnonymous()
    {
        // Arrange
        var currentUser = CreateCurrentUser(new ClaimsPrincipal(new ClaimsIdentity()));

        // Act
        var act = () => currentUser.UserId;

        // Assert
        currentUser.IsAuthenticated.Should().BeFalse();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_NotBeAuthenticated_When_UserIdClaimMalformed()
    {
        // Arrange
        var identity = new ClaimsIdentity([new Claim(DevPulseClaimTypes.UserId, "not-a-guid")], "Cookies");
        var currentUser = CreateCurrentUser(new ClaimsPrincipal(identity));

        // Act
        var isAuthenticated = currentUser.IsAuthenticated;

        // Assert
        isAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Should_NotBeAuthenticated_When_NoHttpContext()
    {
        // Arrange – np. kod uruchomiony poza żądaniem HTTP
        var currentUser = new CurrentUser(new HttpContextAccessor());

        // Act
        var isAuthenticated = currentUser.IsAuthenticated;

        // Assert
        isAuthenticated.Should().BeFalse();
    }

    private static CurrentUser CreateCurrentUser(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
}
