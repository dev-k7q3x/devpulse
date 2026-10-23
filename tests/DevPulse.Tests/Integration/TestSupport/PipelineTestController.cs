using DevPulse.Api.RateLimiting;
using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>Kontroler wyłącznie testowy (application part dodawany w fabryce) – nie istnieje w aplikacji.</summary>
[ApiController]
[Route("api/__test")]
public sealed class PipelineTestController : ControllerBase
{
    [HttpPost("validated")]
    public IActionResult Validated(RepositoryAddRequest request) => NoContent();

    [HttpGet("throw/{kind}")]
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
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    public IActionResult AnonymousLimited() => NoContent();
}
