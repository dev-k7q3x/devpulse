using AspNet.Security.OAuth.GitHub;
using DevPulse.Api.Auth;
using DevPulse.Api.RateLimiting;
using DevPulse.Application.DTOs.Auth;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Api.Controllers;

/// <summary>M1: logowanie GitHub OAuth, sesja i konto. Callback <c>/signin-github</c> obsługuje handler OAuth, nie kontroler.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, ICurrentUser currentUser, IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>Challenge GitHub OAuth – 302 na github.com. <c>returnUrl</c> tylko lokalny (walidator).</summary>
    [HttpGet("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public IActionResult Login([FromQuery] AuthLoginRequest request)
    {
        var properties = new AuthenticationProperties { RedirectUri = request.ReturnUrl ?? "/" };
        return Challenge(properties, GitHubAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>
    /// Zalogowany użytkownik. Anonimowy dostęp do akcji tylko po to, by wydać cookie XSRF-TOKEN także przed
    /// zalogowaniem – bez sesji odpowiedź to nadal 401 <c>unauthenticated</c>.
    /// </summary>
    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<CurrentUserResponse>> GetMe(CancellationToken cancellationToken)
    {
        XsrfToken.Issue(HttpContext, antiforgery);

        if (!currentUser.IsAuthenticated)
        {
            return Unauthenticated();
        }

        var user = await authService.GetCurrentUserAsync(currentUser.UserId, cancellationToken);
        if (user is null)
        {
            // Konto usunięte w innej sesji – cookie jest martwe.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthenticated();
        }

        return Ok(user);
    }

    [HttpDelete("me")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
    {
        await authService.DeleteAccountAsync(currentUser.UserId, cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private ObjectResult Unauthenticated() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        type: ProblemTypes.Unauthenticated,
        detail: "Wymagane logowanie.");
}
