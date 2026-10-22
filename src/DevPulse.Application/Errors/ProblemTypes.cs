namespace DevPulse.Application.Errors;

/// <summary>Stałe slugi pola <c>type</c> w ProblemDetails (plan 03, tabela 0.1) – po nich SPA rozpoznaje błąd.</summary>
public static class ProblemTypes
{
    public const string Validation = "validation";
    public const string InvalidToken = "invalid-token";
    public const string Unauthenticated = "unauthenticated";
    public const string GitHubTokenInvalid = "github-token-invalid";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string LimitExceeded = "limit-exceeded";
    public const string RateLimited = "rate-limited";
    public const string GitHubUnavailable = "github-unavailable";
    public const string GitHubRateLimited = "github-rate-limited";

    /// <summary>
    /// Domyślny slug dla statusu, gdy kod nie ustawił typu jawnie.
    /// 502 i 503 nie mają domyślnego slugu – w tabeli 0.1 dotyczą wyłącznie GitHuba i ustawia je kod.
    /// </summary>
    public static string? ForStatus(int statusCode) => statusCode switch
    {
        400 => Validation,
        401 => Unauthenticated,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        422 => LimitExceeded,
        429 => RateLimited,
        _ => null,
    };
}
