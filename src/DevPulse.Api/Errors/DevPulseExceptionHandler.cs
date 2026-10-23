using System.Globalization;
using DevPulse.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DevPulse.Api.Errors;

/// <summary>Błędy biznesowe z Application → ProblemDetails ze statusem i slugiem z kontraktu (plan 03, tabela 0.1).</summary>
internal sealed class DevPulseExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DevPulseException devPulseException)
        {
            return false;
        }

        if (devPulseException is GitHubRateLimitedException { RetryAfter: { } retryAfter })
        {
            httpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        httpContext.Response.StatusCode = devPulseException.StatusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = devPulseException.StatusCode,
                Type = devPulseException.ProblemType,
                Detail = devPulseException.Message,
            },
        });
    }
}
