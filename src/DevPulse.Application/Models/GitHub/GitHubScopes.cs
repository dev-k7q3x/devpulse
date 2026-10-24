namespace DevPulse.Application.Models.GitHub;

/// <summary>Scopes OAuth wymagane przez DevPulse i porównanie ich z <c>users.token_scopes</c>.</summary>
public static class GitHubScopes
{
    public static IReadOnlyList<string> Required { get; } = ["read:user", "user:email", "repo"];

    // Scope nadrzędny obejmuje podrzędne: "user" daje też read:user i user:email.
    private static readonly Dictionary<string, string> ParentScopes = new(StringComparer.Ordinal)
    {
        ["read:user"] = "user",
        ["user:email"] = "user",
    };

    /// <summary>Scopes z odpowiedzi GitHuba („repo,user:email”, czasem rozdzielone spacją) w postaci „a,b,c”.</summary>
    public static string Normalize(string? scopes) => string.Join(',', Parse(scopes));

    /// <summary>Wymagane scopes, których token nie ma. Niepusta lista = konieczne ponowne logowanie.</summary>
    public static IReadOnlyList<string> Missing(string? tokenScopes)
    {
        var granted = Parse(tokenScopes).ToHashSet(StringComparer.Ordinal);

        return Required
            .Where(scope => !granted.Contains(scope)
                && !(ParentScopes.TryGetValue(scope, out var parent) && granted.Contains(parent)))
            .ToList();
    }

    private static IEnumerable<string> Parse(string? scopes) =>
        (scopes ?? string.Empty)
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal);
}
