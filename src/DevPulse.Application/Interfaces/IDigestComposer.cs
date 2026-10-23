using DevPulse.Application.Digests;

namespace DevPulse.Application.Interfaces;

/// <summary>Czysta logika digestu (bez I/O): podsumowanie stanu repo i fallback regułowy.</summary>
public interface IDigestComposer
{
    /// <param name="repositories">Stan śledzonych repo ze snapshotów.</param>
    /// <param name="digestDate">Lokalna data digestu.</param>
    /// <param name="now">Bieżący czas UTC – do oceny wieku PR-ów i aktywności.</param>
    /// <returns>Model z priorytetami regułowymi (failing CI &gt; stare PR-y &gt; brak aktywności), maks. 5.</returns>
    DigestComposition Compose(IReadOnlyList<RepositoryDigestState> repositories, DateOnly digestDate, DateTimeOffset now);
}
