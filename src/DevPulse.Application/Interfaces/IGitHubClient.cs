using DevPulse.Application.Models.GitHub;

namespace DevPulse.Application.Interfaces;

/// <summary>
/// Klient GitHub API (GraphQL do snapshotów, REST + ETag do list). Przyjmuje <c>userId</c> jawnie – token i budżet
/// rate limitu są per użytkownik, a joby nie mają HttpContext.
/// Błędy: <see cref="Errors.GitHubTokenInvalidException"/> (401), <see cref="Errors.GitHubRateLimitedException"/>
/// (rezerwa budżetu, secondary limit), <see cref="Errors.GitHubUnavailableException"/> (5xx, timeout).
/// </summary>
public interface IGitHubClient
{
    /// <summary>Wszystkie repo dostępne dla użytkownika (wszystkie strony <c>GET /user/repos</c>, ETag per strona).</summary>
    Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken);

    /// <returns><c>null</c>, gdy repo nie istnieje albo użytkownik nie ma do niego dostępu.</returns>
    Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken);

    /// <summary>Stan wielu repo przez <c>nodes(ids:)</c> w paczkach po 20.</summary>
    Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
        Guid userId,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken);
}
