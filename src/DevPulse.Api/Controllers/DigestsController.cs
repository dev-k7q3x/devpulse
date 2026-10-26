using DevPulse.Application.DTOs.Digests;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DevPulse.Api.Controllers;

/// <summary>M10: historia digestów z ostatnich 30 dni (plan 03 §5). Cudzy digest → 404.</summary>
[ApiController]
[Route("api/digests")]
public sealed class DigestsController(IDigestService digestService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// HTML maila trafia wyłącznie do <c>&lt;iframe sandbox&gt;</c>: CSP <c>sandbox</c> bez skryptów i bez zasobów z zewnątrz
    /// poza obrazkami https/data. Treść po zapisie się nie zmienia, więc cache jest <c>immutable</c>.
    /// </summary>
    public const string HtmlContentSecurityPolicy = "sandbox; default-src 'none'; img-src https: data:; style-src 'unsafe-inline'";

    public const string HtmlCacheControl = "private, max-age=86400, immutable";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DigestListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<DigestListItem>>> GetList(CancellationToken cancellationToken) =>
        Ok(await digestService.GetListAsync(currentUser.UserId, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DigestDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<DigestDetailResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await digestService.GetAsync(currentUser.UserId, id, cancellationToken));

    [HttpGet("{id:guid}/html")]
    [Produces("text/html")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetHtml(Guid id, CancellationToken cancellationToken)
    {
        var html = await digestService.GetHtmlAsync(currentUser.UserId, id, cancellationToken);

        // Nagłówki dopiero po udanym odczycie – 404 nie może dostać cache immutable.
        Response.Headers.ContentSecurityPolicy = HtmlContentSecurityPolicy;
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = HtmlCacheControl;

        return Content(html, "text/html; charset=utf-8");
    }
}
