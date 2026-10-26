using DevPulse.Api.Auth;
using Microsoft.Net.Http.Headers;

namespace DevPulse.Api.Security;

/// <summary>
/// Nagłówki bezpieczeństwa: <c>nosniff</c> i <c>Referrer-Policy</c> dla każdej odpowiedzi oraz CSP dla stron SPA.
/// CSP nie nadpisuje nagłówka ustawionego przez endpoint (podgląd digestu ma własny, z <c>sandbox</c>), a panel
/// <c>/hangfire</c> go nie dostaje – używa skryptów inline.
/// </summary>
internal static class SecurityHeadersMiddleware
{
    /// <summary>
    /// SPA: skrypty i style tylko z własnego originu (build Vite nie ma kodu inline), avatary z GitHuba,
    /// podgląd digestu w <c>iframe</c> z tego samego originu, aplikacja nie daje się osadzić gdzie indziej.
    /// </summary>
    public const string SpaContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: https://avatars.githubusercontent.com; " +
        "connect-src 'self'; frame-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; " +
        "frame-ancestors 'none'";

    public const string ReferrerPolicy = "strict-origin-when-cross-origin";

    public static IApplicationBuilder UseDevPulseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // Ścieżka z wejścia: endpointy jak panel Hangfire przenoszą prefiks do PathBase przed wysłaniem odpowiedzi.
            var requestPath = context.Request.PathBase.Add(context.Request.Path);

            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.TryAdd(HeaderNames.XContentTypeOptions, "nosniff");
                headers.TryAdd("Referrer-Policy", ReferrerPolicy);

                if (IsSpaResponse(requestPath, context.Response.ContentType))
                {
                    headers.TryAdd(HeaderNames.ContentSecurityPolicy, SpaContentSecurityPolicy);
                }

                return Task.CompletedTask;
            });

            return next(context);
        });

    private static bool IsSpaResponse(PathString path, string? contentType)
    {
        if (path.StartsWithSegments("/api") || path.StartsWithSegments(AuthServiceCollectionExtensions.HangfireDashboardPath) || path.StartsWithSegments("/health"))
        {
            return false;
        }

        return contentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }
}
