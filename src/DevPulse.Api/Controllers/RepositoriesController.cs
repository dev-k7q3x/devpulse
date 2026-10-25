using DevPulse.Api.RateLimiting;
using DevPulse.Application.DTOs.Repositories;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Api.Controllers;

/// <summary>M3: śledzone repozytoria. <c>available</c> i <c>POST</c> mogą wołać GitHuba; lista śledzonych czyta tylko bazę.</summary>
[ApiController]
[Route("api/repositories")]
public sealed class RepositoriesController(IRepositoryService repositoryService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Repo dostępne na GitHubie (cache 10 min). <c>refresh=true</c> pomija cache i liczy się do ostrzejszego limitu.</summary>
    [HttpGet("available")]
    [EnableRateLimiting(RateLimitPolicies.GitHubRead)]
    [ProducesResponseType<AvailableRepositoriesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AvailableRepositoriesResponse>> GetAvailable(
        [FromQuery] AvailableRepositoriesRequest request,
        CancellationToken cancellationToken) =>
        Ok(await repositoryService.GetAvailableAsync(currentUser.UserId, request, cancellationToken));

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TrackedRepositoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<TrackedRepositoryResponse>>> GetTracked(CancellationToken cancellationToken) =>
        Ok(await repositoryService.GetTrackedAsync(currentUser.UserId, cancellationToken));

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.RepoMutations)]
    [ProducesResponseType<TrackedRepositoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<TrackedRepositoryResponse>> Add(RepositoryAddRequest request, CancellationToken cancellationToken)
    {
        var repository = await repositoryService.AddAsync(currentUser.UserId, request, cancellationToken);
        return Created($"/api/repositories/{repository.Id}", repository);
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.RepoMutations)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await repositoryService.RemoveAsync(currentUser.UserId, id, cancellationToken);
        return NoContent();
    }
}
