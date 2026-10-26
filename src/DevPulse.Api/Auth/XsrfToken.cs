using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace DevPulse.Api.Auth;

/// <summary>
/// Antiforgery double-submit: SPA czyta cookie <see cref="CookieName"/> i odsyła je w nagłówku <see cref="HeaderName"/>
/// przy POST/PUT/DELETE. Token jest związany z tożsamością, więc wydaje go <c>GET /api/auth/me</c> – SPA woła go
/// przy starcie, po zalogowaniu i po wylogowaniu.
/// </summary>
internal static class XsrfToken
{
    public const string CookieName = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>Ciasteczko z drugą połówką pary (HttpOnly) – wartość z <see cref="CookieName"/> jest z nią porównywana.</summary>
    public const string AntiforgeryCookieName = "devpulse_antiforgery";

    public static void Issue(HttpContext httpContext, IAntiforgery antiforgery)
    {
        // Ta sama polityka Secure co cookie antiforgery (Always poza Development).
        var securePolicy = httpContext.RequestServices.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy;

        // Poza Development tylko HTTPS (za proxy – X-Forwarded-Proto). Żądanie po http (np. wprost na port kontenera)
        // nie dostaje tokenu zamiast 500 z GetAndStoreTokens; jego mutacje i tak odrzuci walidacja antiforgery.
        if (securePolicy == CookieSecurePolicy.Always && !httpContext.Request.IsHttps)
        {
            return;
        }

        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        if (tokens.RequestToken is null)
        {
            return;
        }

        // Celowo bez HttpOnly – JavaScript SPA musi odczytać wartość.
        httpContext.Response.Cookies.Append(CookieName, tokens.RequestToken, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = securePolicy == CookieSecurePolicy.Always
                || (securePolicy == CookieSecurePolicy.SameAsRequest && httpContext.Request.IsHttps),
            Path = "/",
        });
    }
}
