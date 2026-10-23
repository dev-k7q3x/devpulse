namespace DevPulse.Application.Errors;

/// <summary>
/// Błąd biznesowy z kontraktu (plan 03, tabela 0.1). Api mapuje go na ProblemDetails ze statusem i slugiem <see cref="ProblemType"/>.
/// <see cref="Exception.Message"/> trafia do <c>detail</c> – nie umieszczaj w nim danych wrażliwych.
/// </summary>
public abstract class DevPulseException(int statusCode, string problemType, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;

    public string ProblemType { get; } = problemType;
}

/// <summary>Brak zasobu albo zasób innego użytkownika (404, nie 403 – nie zdradzamy istnienia).</summary>
public sealed class NotFoundException(string message = "Nie znaleziono zasobu.")
    : DevPulseException(404, ProblemTypes.NotFound, message);

/// <summary>Naruszenie unikalności, np. repo jest już śledzone.</summary>
public sealed class ConflictException(string message)
    : DevPulseException(409, ProblemTypes.Conflict, message);

/// <summary>Przekroczony limit biznesowy, np. maksymalna liczba repo.</summary>
public sealed class LimitExceededException(string message)
    : DevPulseException(422, ProblemTypes.LimitExceeded, message);

/// <summary>Nieprawidłowy, wygasły albo użyty token potwierdzenia emaila – jeden komunikat dla wszystkich przypadków.</summary>
public sealed class InvalidTokenException()
    : DevPulseException(400, ProblemTypes.InvalidToken, "Link potwierdzający jest nieprawidłowy albo wygasł.");

/// <summary>GitHub odrzucił token (401). Zwracane tylko przez endpointy, które wołają GitHuba.</summary>
public sealed class GitHubTokenInvalidException()
    : DevPulseException(401, ProblemTypes.GitHubTokenInvalid, "Token GitHub jest nieważny. Zaloguj się ponownie.");

/// <summary>GitHub zwrócił 5xx albo timeout.</summary>
public sealed class GitHubUnavailableException(string message = "GitHub jest chwilowo niedostępny.", Exception? innerException = null)
    : DevPulseException(502, ProblemTypes.GitHubUnavailable, message, innerException);

/// <summary>Kończy się budżet GitHub API użytkownika albo secondary rate limit. Api ustawia nagłówek <c>Retry-After</c>.</summary>
public sealed class GitHubRateLimitedException(TimeSpan? retryAfter)
    : DevPulseException(503, ProblemTypes.GitHubRateLimited, "Limit zapytań do GitHuba jest chwilowo wyczerpany.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
