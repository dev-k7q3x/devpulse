using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPulse.Api.Auth;

/// <summary>
/// Cookie sesji (HttpOnly, SameSite=Lax) + GitHub OAuth, FallbackPolicy = zalogowany użytkownik, antiforgery double-submit.
/// Cookie, a nie JWT: SPA i API działają na jednej domenie (plan 01, decyzja 2).
/// </summary>
internal static class AuthServiceCollectionExtensions
{
    public const string SessionCookieName = "devpulse_session";
    public const string LoginPagePath = "/login";
    public const string HangfireDashboardPath = "/hangfire";

    public static IServiceCollection AddDevPulseAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        // Fail fast: handler OAuth bez klienta rzuca przy KAŻDYM żądaniu (middleware sprawdza jego callback path).
        if (string.IsNullOrWhiteSpace(options.GitHub.ClientId) || string.IsNullOrWhiteSpace(options.GitHub.ClientSecret))
        {
            throw new InvalidOperationException(
                "Brak konfiguracji GitHub OAuth (Auth:GitHub:ClientId i Auth:GitHub:ClientSecret – user-secrets lokalnie, " +
                "zmienne Auth__GitHub__ClientId i Auth__GitHub__ClientSecret na Railway).");
        }

        // Poza Development flaga Secure zawsze – nie zależy od poprawnego X-Forwarded-Proto z proxy Railway.
        // Lokalnie SameAsRequest: dev działa po http.
        var cookieSecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = SessionCookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = cookieSecurePolicy;
                cookie.ExpireTimeSpan = options.SessionLifetime;
                cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToLogin = context => OnRedirectToLoginAsync(context);
                cookie.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(
                    context.HttpContext, StatusCodes.Status403Forbidden, ProblemTypes.Forbidden, "Brak uprawnień.");
            })
            .AddGitHub(github =>
            {
                github.ClientId = options.GitHub.ClientId;
                github.ClientSecret = options.GitHub.ClientSecret;
                github.CallbackPath = GitHubAuthenticationDefaults.CallbackPath;
                foreach (var scope in GitHubScopes.Required)
                {
                    github.Scope.Add(scope);
                }

                github.ClaimActions.MapJsonKey(GitHubClaimsMapper.AvatarUrlClaimType, "avatar_url");
                github.SaveTokens = false;

                // Domyślne SameSite=None wymaga HTTPS – Lax wystarcza dla powrotu z GitHuba (nawigacja top-level GET).
                github.CorrelationCookie.SameSite = SameSiteMode.Lax;

                github.Events.OnCreatingTicket = OnCreatingTicketAsync;
                github.Events.OnAccessDenied = OnAccessDenied;
                github.Events.OnRemoteFailure = OnRemoteFailure;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AdminRequirement()));
        services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = XsrfToken.HeaderName;
            antiforgery.Cookie.Name = XsrfToken.AntiforgeryCookieName;
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
            antiforgery.Cookie.SecurePolicy = cookieSecurePolicy;
        });
        services.AddScoped<AntiforgeryValidationFilter>();

        return services;
    }

    /// <summary>
    /// Upsert użytkownika i podmiana principala: do cookie trafia tylko id DevPulse, token GitHub idzie zaszyfrowany do bazy.
    /// </summary>
    private static async Task OnCreatingTicketAsync(OAuthCreatingTicketContext context)
    {
        var accessToken = context.AccessToken
            ?? throw new InvalidOperationException("GitHub nie zwrócił access tokenu.");
        var gitHubPrincipal = context.Principal
            ?? throw new InvalidOperationException("Brak profilu użytkownika GitHub.");

        // Faktycznie przyznane scopes – użytkownik mógł zawęzić je na ekranie zgody.
        var grantedScopes = context.TokenResponse.Response?.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String
                ? scope.GetString()
                : null;

        var login = GitHubClaimsMapper.ToGitHubLogin(gitHubPrincipal, accessToken, grantedScopes);
        var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var userId = await authService.SignInWithGitHubAsync(login, context.HttpContext.RequestAborted);

        context.Principal = GitHubClaimsMapper.CreateSessionPrincipal(userId);
    }

    private static Task OnAccessDenied(AccessDeniedContext context)
    {
        context.Response.Redirect($"{LoginPagePath}?error=access_denied");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static Task OnRemoteFailure(RemoteFailureContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthServiceCollectionExtensions));
        logger.LogWarning(context.Failure, "GitHub OAuth callback failed");

        context.Response.Redirect($"{LoginPagePath}?error=login_failed");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// API i panel <c>/hangfire</c> odpowiadają 401 ProblemDetails, nie 302 (plan 03 §0 i §6) – SPA sama przechodzi na /login.
    /// </summary>
    private static Task OnRedirectToLoginAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments(HangfireDashboardPath))
        {
            context.Response.Redirect(LoginPagePath);
            return Task.CompletedTask;
        }

        return WriteProblemAsync(
            context.HttpContext, StatusCodes.Status401Unauthorized, ProblemTypes.Unauthenticated, "Wymagane logowanie.");
    }

    private static async Task WriteProblemAsync(HttpContext httpContext, int statusCode, string type, string detail)
    {
        httpContext.Response.StatusCode = statusCode;
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = statusCode, Type = type, Detail = detail },
        });
    }
}
