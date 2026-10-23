using DevPulse.Application.Digests;

namespace DevPulse.Application.Interfaces;

/// <summary>
/// Priorytety digestu z Anthropic (maks. 5 po jednym zdaniu, wyjście strukturalne, limit tokenów, timeout).
/// Woła go wyłącznie SendDigestJob – żaden endpoint HTTP.
/// </summary>
public interface IAiPrioritizer
{
    /// <returns><c>null</c> przy błędzie, timeoucie albo nieprawidłowym JSON – wtedy digest używa fallbacku regułowego.</returns>
    Task<AiPrioritizationResult?> PrioritizeAsync(
        IReadOnlyList<RepositoryDigestState> repositories,
        CancellationToken cancellationToken);
}
