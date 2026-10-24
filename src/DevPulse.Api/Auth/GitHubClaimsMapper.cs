using System.Globalization;
using System.Security.Claims;
using DevPulse.Application.Models.GitHub;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DevPulse.Api.Auth;

/// <summary>
/// Claimy z profilu GitHub (handler OAuth) → <see cref="GitHubLogin"/>, a id DevPulse → principal cookie sesji.
/// Cookie niesie wyłącznie id użytkownika DevPulse – żadnych danych GitHuba ani tokenu.
/// </summary>
internal static class GitHubClaimsMapper
{
    /// <summary>Handler GitHub nie mapuje <c>avatar_url</c> – dodajemy własny claim.</summary>
    public const string AvatarUrlClaimType = "urn:github:avatar_url";

    public static GitHubLogin ToGitHubLogin(ClaimsPrincipal gitHubPrincipal, string accessToken, string? grantedScopes)
    {
        ArgumentNullException.ThrowIfNull(gitHubPrincipal);
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        var gitHubIdValue = gitHubPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(gitHubIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var gitHubId) || gitHubId <= 0)
        {
            throw new InvalidOperationException("Profil GitHub nie zawiera poprawnego id użytkownika.");
        }

        var login = gitHubPrincipal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new InvalidOperationException("Profil GitHub nie zawiera loginu.");
        }

        return new GitHubLogin(
            gitHubId,
            login,
            NullIfWhiteSpace(gitHubPrincipal.FindFirstValue(AvatarUrlClaimType)),
            NullIfWhiteSpace(gitHubPrincipal.FindFirstValue(ClaimTypes.Email)),
            accessToken,
            grantedScopes ?? string.Empty);
    }

    public static ClaimsPrincipal CreateSessionPrincipal(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Brak id użytkownika.", nameof(userId));
        }

        var identity = new ClaimsIdentity(
            [new Claim(DevPulseClaimTypes.UserId, userId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
