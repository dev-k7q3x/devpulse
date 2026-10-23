using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Domain.ValueObjects;

namespace DevPulse.Application.Digests;

/// <summary>Stan repo jako wejście digestu (i promptu AI) – tylko liczby i tytuły, bez diffów.</summary>
public sealed record RepositoryDigestState(
    string RepoFullName,
    int OpenPrCount,
    int OpenIssueCount,
    CiStatus CiStatus,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? OldestOpenPrCreatedAt);

/// <summary>Model digestu przed priorytetami AI i renderem HTML.</summary>
public sealed record DigestComposition(
    string Subject,
    IReadOnlyList<DigestRepositorySummary> RepositorySummary,
    IReadOnlyList<DigestPriority> FallbackPriorities);

/// <summary>Priorytety z AI wraz z metadanymi kosztu.</summary>
public sealed record AiPrioritizationResult(IReadOnlyList<DigestPriority> Priorities, DigestAiUsage Usage);
