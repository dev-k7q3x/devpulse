using DevPulse.Application.DTOs.Dashboard;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DevPulse.Api.Controllers;

/// <summary>M5: dashboard z samych snapshotów w bazie – nigdy nie woła GitHuba, więc nie zwraca github-token-invalid.</summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Karty śledzonych repo. ETag + <c>Cache-Control: private, no-cache</c> – zwykłe odpytanie co 15 min kończy się 304.</summary>
    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var etag = await dashboardService.GetVersionAsync(userId, cancellationToken);

        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.ETag = etag;

        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(new EntityTagHeaderValue(etag), useStrongComparison: false)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(await dashboardService.GetAsync(userId, cancellationToken));
    }
}
