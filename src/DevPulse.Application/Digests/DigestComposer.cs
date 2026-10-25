using System.Globalization;
using DevPulse.Application.Interfaces;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DevPulse.Application.Digests;

/// <summary>
/// Czysta logika digestu: podsumowanie stanu repo, temat maila i priorytety regułowe.
/// Kolejność reguł: failing CI &gt; stare PR-y &gt; brak aktywności. Jedno repo = najwyżej jeden priorytet
/// (najważniejsza reguła), żeby jedno zaniedbane repo nie zajęło wszystkich pięciu miejsc.
/// </summary>
public sealed class DigestComposer(IOptions<DigestRulesOptions> options) : IDigestComposer
{
    private readonly DigestRulesOptions _rules = options.Value;

    public DigestComposition Compose(IReadOnlyList<RepositoryDigestState> repositories, DateOnly digestDate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var ordered = repositories
            .OrderBy(r => r.RepoFullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summary = ordered
            .Select(r => new DigestRepositorySummary(r.RepoFullName, r.OpenPrCount, r.OpenIssueCount, r.CiStatus, r.LastActivityAt))
            .ToList();

        var candidates = ordered
            .Select(r => Evaluate(r, now))
            .OfType<Candidate>()
            .ToList();

        var priorities = candidates
            .OrderBy(c => c.Rule)
            .ThenByDescending(c => c.SortWeight)
            .ThenBy(c => c.Repository.RepoFullName, StringComparer.OrdinalIgnoreCase)
            .Take(Digest.MaxPriorities)
            .Select(c => c.Priority)
            .ToList();

        return new DigestComposition(BuildSubject(digestDate, ordered.Count, candidates.Count), summary, priorities);
    }

    private Candidate? Evaluate(RepositoryDigestState repository, DateTimeOffset now)
    {
        if (repository.CiStatus == CiStatus.Failure)
        {
            // Więcej otwartych PR-ów = więcej zablokowanej pracy, więc wyżej.
            return new Candidate(
                Rule.FailingCi,
                repository.OpenPrCount,
                repository,
                new DigestPriority(
                    repository.RepoFullName,
                    "CI na domyślnej gałęzi nie przechodzi – napraw build, zanim zablokuje kolejne zmiany.",
                    PrioritySeverity.High));
        }

        if (repository.OpenPrCount > 0
            && repository.OldestOpenPrCreatedAt is { } oldestPr
            && now - oldestPr >= _rules.StalePullRequestAfter)
        {
            var days = WholeDays(now - oldestPr);
            return new Candidate(
                Rule.StalePullRequest,
                days,
                repository,
                new DigestPriority(
                    repository.RepoFullName,
                    $"Najstarszy otwarty PR czeka {PolishText.Days(days)} – przejrzyj go albo zamknij ({PolishText.OpenPullRequests(repository.OpenPrCount)}).",
                    PrioritySeverity.Medium));
        }

        // Brak daty = brak danych (snapshot jeszcze nie pobrany), a nie brak aktywności.
        if (repository.LastActivityAt is { } lastActivity && now - lastActivity >= _rules.InactiveAfter)
        {
            var days = WholeDays(now - lastActivity);
            return new Candidate(
                Rule.Inactive,
                days,
                repository,
                new DigestPriority(
                    repository.RepoFullName,
                    $"Brak aktywności od {PolishText.Days(days)} – sprawdź, czy repozytorium nadal wymaga śledzenia.",
                    PrioritySeverity.Low));
        }

        return null;
    }

    private static string BuildSubject(DateOnly digestDate, int repositoryCount, int needingAttention)
    {
        var date = digestDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        if (repositoryCount == 0)
        {
            return $"DevPulse – {date}: brak śledzonych repozytoriów";
        }

        return needingAttention == 0
            ? $"DevPulse – {date}: wszystkie repozytoria w porządku"
            : $"DevPulse – {date}: uwagi wymaga {needingAttention} z {PolishText.RepositoriesGenitive(repositoryCount)}";
    }

    private static int WholeDays(TimeSpan age) => (int)Math.Floor(age.TotalDays);

    private enum Rule
    {
        FailingCi,
        StalePullRequest,
        Inactive,
    }

    private sealed record Candidate(Rule Rule, int SortWeight, RepositoryDigestState Repository, DigestPriority Priority);
}
