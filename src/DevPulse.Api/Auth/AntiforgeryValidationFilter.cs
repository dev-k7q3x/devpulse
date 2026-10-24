using DevPulse.Application.Errors;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DevPulse.Api.Auth;

/// <summary>
/// Globalny filtr kontrolerów: POST/PUT/PATCH/DELETE bez poprawnego <see cref="XsrfToken.HeaderName"/> → 400 <c>validation</c>.
/// Biegnie po UseAuthorization, więc niezalogowany klient dostaje najpierw 401.
/// </summary>
internal sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (context.Result is not null
            || HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Type = ProblemTypes.Validation,
                Detail = $"Brak albo nieprawidłowy nagłówek {XsrfToken.HeaderName}.",
            };
            context.Result = new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        }
    }
}
