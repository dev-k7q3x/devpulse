using System.Globalization;
using System.Net;
using DevPulse.Application.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.GitHub;

/// <summary>
/// Budżet i błędy GitHub API w jednym miejscu (plan 03 §7.2):
/// rezerwa budżetu dla jobów, odczyt <c>x-ratelimit-*</c>, 401 → <c>token_invalid</c>, 403/429 z limitem → 503,
/// 5xx i błędy sieci → 502. Pozostałe odpowiedzi (np. 404) wracają do <see cref="GitHubClient"/>.
/// </summary>
internal sealed class GitHubRateLimitHandler(
    GitHubRateLimitBudget budget,
    IGitHubTokenStore tokenStore,
    IOptions<GitHubOptions> options,
    TimeProvider timeProvider,
    ILogger<GitHubRateLimitHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(GitHubRequestOptions.UserId, out var userId))
        {
            throw new InvalidOperationException("Żądanie do GitHuba bez userId – budżet limitu jest per użytkownik.");
        }

        var interactive = request.Options.TryGetValue(GitHubRequestOptions.Interactive, out var isInteractive) && isInteractive;
        var now = timeProvider.GetUtcNow();

        // Żądania interaktywne idą tylko do REST (core). Joby nie są blokowane – po to jest rezerwa.
        if (interactive
            && budget.IsBelowReserve(userId, GitHubRateLimitBudget.CoreResource, options.Value.RateLimitReserve, now, out var reserveRetryAfter))
        {
            throw new GitHubRateLimitedException(reserveRetryAfter);
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubUnavailableException(innerException: exception);
        }

        var rateLimit = ReadRateLimit(response);
        if (rateLimit is { } limit)
        {
            budget.Record(userId, limit.Resource, limit.Remaining, limit.ResetAt);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await tokenStore.MarkTokenInvalidAsync(userId, cancellationToken);
            throw new GitHubTokenInvalidException();
        }

        if (IsRateLimited(response, rateLimit))
        {
            var retryAfter = ReadRetryAfter(response, now) ?? (rateLimit is { } l ? l.ResetAt - now : null);
            logger.LogWarning(
                "GitHub rate limit hit for user {UserId} ({StatusCode}), retry after {RetryAfter}",
                userId,
                (int)response.StatusCode,
                retryAfter);
            response.Dispose();
            throw new GitHubRateLimitedException(retryAfter is { } r && r > TimeSpan.Zero ? r : null);
        }

        if ((int)response.StatusCode >= 500)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new GitHubUnavailableException($"GitHub zwrócił błąd {statusCode}.");
        }

        return response;
    }

    /// <summary>Primary limit (403/429 z <c>remaining = 0</c>) albo secondary limit (403/429 z <c>retry-after</c>) – to nie brak uprawnień.</summary>
    private static bool IsRateLimited(HttpResponseMessage response, RateLimit? rateLimit) =>
        response.StatusCode == HttpStatusCode.TooManyRequests
        || (response.StatusCode == HttpStatusCode.Forbidden
            && (response.Headers.RetryAfter is not null || rateLimit is { Remaining: 0 }));

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset now) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - now,
        _ => null,
    };

    private static RateLimit? ReadRateLimit(HttpResponseMessage response)
    {
        if (!TryReadLong(response, "x-ratelimit-remaining", out var remaining) || !TryReadLong(response, "x-ratelimit-reset", out var reset))
        {
            return null;
        }

        var resource = response.Headers.TryGetValues("x-ratelimit-resource", out var values)
            ? values.FirstOrDefault() ?? GitHubRateLimitBudget.CoreResource
            : GitHubRateLimitBudget.CoreResource;

        return new RateLimit(resource, (int)Math.Clamp(remaining, 0, int.MaxValue), DateTimeOffset.FromUnixTimeSeconds(reset));
    }

    private static bool TryReadLong(HttpResponseMessage response, string header, out long value)
    {
        value = 0;
        return response.Headers.TryGetValues(header, out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct RateLimit(string Resource, int Remaining, DateTimeOffset ResetAt);
}
