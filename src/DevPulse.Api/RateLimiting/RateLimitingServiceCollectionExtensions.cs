using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using DevPulse.Api.Auth;
using DevPulse.Application.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevPulse.Api.RateLimiting;

/// <summary>
/// Rate limiter w pamięci – poprawny przy jednej instancji Railway. Partycje per userId (claim
/// <see cref="DevPulseClaimTypes.UserId"/>), a dla anonimowych per IP (za proxy wymaga ForwardedHeaders).
/// </summary>
internal static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddDevPulseRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.AddPolicy(RateLimitPolicies.Default, context => RateLimitPartition.GetTokenBucketLimiter(
                UserOrIpKey(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.DefaultPerMinute,
                    TokensPerPeriod = limits.DefaultPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Jeden endpoint (available), dwa limity: refresh=true pomija cache i zawsze kosztuje budżet GitHuba,
            // więc trafia do osobnej, ostrzejszej partycji github-refresh.
            options.AddPolicy(RateLimitPolicies.GitHubRead, context => IsRefreshRequest(context)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    $"{RateLimitPolicies.GitHubRefresh}:{UserOrIpKey(context)}",
                    _ => FixedWindow(limits.GitHubRefreshPerMinute, TimeSpan.FromMinutes(1)))
                : RateLimitPartition.GetFixedWindowLimiter(
                    $"{RateLimitPolicies.GitHubRead}:{UserOrIpKey(context)}",
                    _ => FixedWindow(limits.GitHubReadPerMinute, TimeSpan.FromMinutes(1))));
            AddFixedWindowPolicy(options, RateLimitPolicies.GitHubRefresh, limits.GitHubRefreshPerMinute, TimeSpan.FromMinutes(1), UserOrIpKey);
            AddFixedWindowPolicy(options, RateLimitPolicies.RepoMutations, limits.RepoMutationsPerMinute, TimeSpan.FromMinutes(1), UserOrIpKey);
            AddFixedWindowPolicy(options, RateLimitPolicies.Anonymous, limits.AnonymousPerMinute, TimeSpan.FromMinutes(1), IpKey);
            AddFixedWindowPolicy(options, RateLimitPolicies.Sensitive, limits.SensitivePerHour, TimeSpan.FromHours(1), UserOrIpKey);

            // Dwa limity naraz (godzinowy i dobowy) – oba muszą przepuścić żądanie.
            options.AddPolicy(RateLimitPolicies.Email, context => RateLimitPartition.Get(
                UserOrIpKey(context),
                _ => RateLimiter.CreateChained(
                    new FixedWindowRateLimiter(FixedWindow(limits.EmailPerHour, TimeSpan.FromHours(1))),
                    new FixedWindowRateLimiter(FixedWindow(limits.EmailPerDay, TimeSpan.FromDays(1))))));
        });

        return services;
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        int permitLimit,
        TimeSpan window,
        Func<HttpContext, string> partitionKey) =>
        options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey(context),
            _ => FixedWindow(permitLimit, window)));

    private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit, TimeSpan window) => new()
    {
        PermitLimit = permitLimit,
        Window = window,
        QueueLimit = 0,
    };

    private static bool IsRefreshRequest(HttpContext context) =>
        bool.TryParse(context.Request.Query["refresh"], out var refresh) && refresh;

    private static string UserOrIpKey(HttpContext context) =>
        context.User.FindFirstValue(DevPulseClaimTypes.UserId) is { } userId ? $"user:{userId}" : IpKey(context);

    private static string IpKey(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Type = ProblemTypes.RateLimited,
                Detail = "Zbyt wiele żądań. Spróbuj ponownie później.",
            },
        });
    }
}
