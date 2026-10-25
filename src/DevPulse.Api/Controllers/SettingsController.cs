using DevPulse.Api.RateLimiting;
using DevPulse.Application.DTOs.Settings;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Api.Controllers;

/// <summary>M6: ustawienia digestu i zmiana adresu email (plan 03 §4).</summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settingsService, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await settingsService.GetAsync(currentUser.UserId, cancellationToken));

    [HttpPut]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<SettingsResponse>> Update(SettingsUpdateRequest request, CancellationToken cancellationToken) =>
        Ok(await settingsService.UpdateAsync(currentUser.UserId, request, cancellationToken));

    /// <summary>Rozpoczyna zmianę adresu – mail z linkiem idzie przez Hangfire, stąd 202.</summary>
    [HttpPost("email")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<SettingsResponse>> StartEmailChange(EmailChangeRequest request, CancellationToken cancellationToken) =>
        Accepted(await settingsService.StartEmailChangeAsync(currentUser.UserId, request, cancellationToken));

    [HttpDelete("email/pending")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> CancelEmailChange(CancellationToken cancellationToken)
    {
        await settingsService.CancelEmailChangeAsync(currentUser.UserId, cancellationToken);
        return NoContent();
    }

    /// <summary>Bez logowania – mail może zostać otwarty na innym urządzeniu. Wiersz szukany po SHA-256 tokenu.</summary>
    [HttpPost("email/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    [ProducesResponseType<EmailConfirmResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<EmailConfirmResponse>> ConfirmEmail(EmailConfirmRequest request, CancellationToken cancellationToken) =>
        Ok(await settingsService.ConfirmEmailAsync(request, cancellationToken));
}
