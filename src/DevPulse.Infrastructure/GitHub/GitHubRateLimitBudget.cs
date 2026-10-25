using System.Collections.Concurrent;

namespace DevPulse.Infrastructure.GitHub;

/// <summary>
/// Ostatnio widziany stan limitu GitHuba per użytkownik i zasób (<c>core</c> dla REST, <c>graphql</c>) – z nagłówków
/// <c>x-ratelimit-*</c> każdej odpowiedzi. W pamięci procesu: poprawne przy jednej instancji (jak rate limiter API).
/// </summary>
internal sealed class GitHubRateLimitBudget
{
    public const string CoreResource = "core";

    private readonly ConcurrentDictionary<(Guid UserId, string Resource), Snapshot> _budgets = new();

    public void Record(Guid userId, string resource, int remaining, DateTimeOffset resetAt) =>
        _budgets[(userId, resource)] = new Snapshot(remaining, resetAt);

    /// <summary>
    /// <c>true</c>, gdy znany budżet spadł poniżej rezerwy, a okno limitu jeszcze się nie zresetowało.
    /// Nieznany albo wygasły stan nie blokuje – pierwsza odpowiedź i tak go uzupełni.
    /// </summary>
    public bool IsBelowReserve(Guid userId, string resource, int reserve, DateTimeOffset now, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_budgets.TryGetValue((userId, resource), out var snapshot) || snapshot.ResetAt <= now || snapshot.Remaining >= reserve)
        {
            return false;
        }

        retryAfter = snapshot.ResetAt - now;
        return true;
    }

    private sealed record Snapshot(int Remaining, DateTimeOffset ResetAt);
}
