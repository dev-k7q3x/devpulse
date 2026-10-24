using DevPulse.Api.Auth;
using DevPulse.Api.RateLimiting;
using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Errors;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>
/// Kontroler wyłącznie testowy (application part dodawany w fabryce) – nie istnieje w aplikacji.
/// Akcje bez [AllowAnonymous] wymagają sesji (FallbackPolicy), a POST także nagłówka X-XSRF-TOKEN.
/// </summary>
[ApiController]
[Route("api/__test")]
public sealed class PipelineTestController : ControllerBase
{
    [HttpPost("validated")]
    public IActionResult Validated(RepositoryAddRequest request) => NoContent();

    /// <summary>
    /// Prawdziwe cookie sesji bez przechodzenia przez GitHub OAuth. GET – żeby nie potrzebować tokenu XSRF.
    /// Od razu wydaje XSRF-TOKEN dla nowej tożsamości – także dla id spoza bazy (/api/auth/me zakończyłby taką sesję).
    /// </summary>
    [HttpGet("sign-in/{userId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> SignIn(Guid userId, [FromServices] IAntiforgery antiforgery)
    {
        var principal = GitHubClaimsMapper.CreateSessionPrincipal(userId);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        HttpContext.User = principal;
        XsrfToken.Issue(HttpContext, antiforgery);
        return NoContent();
    }

    [HttpGet("throw/{kind}")]
    [AllowAnonymous]
    public IActionResult Throw(string kind) => kind switch
    {
        "not-found" => throw new NotFoundException(),
        "conflict" => throw new ConflictException("Repozytorium jest już śledzone."),
        "limit-exceeded" => throw new LimitExceededException("Osiągnięto limit 50 repozytoriów."),
        "invalid-token" => throw new InvalidTokenException(),
        "github-token-invalid" => throw new GitHubTokenInvalidException(),
        "github-unavailable" => throw new GitHubUnavailableException(),
        "github-rate-limited" => throw new GitHubRateLimitedException(TimeSpan.FromSeconds(90.2)),
        "unexpected" => throw new InvalidOperationException("sekret-nie-moze-wyciec"),
        _ => Ok(),
    };

    [HttpGet("anonymous-limited")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    public IActionResult AnonymousLimited() => NoContent();
}
