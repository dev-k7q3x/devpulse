using DevPulse.Domain.Enums;

namespace DevPulse.Domain.Entities;

/// <summary>Stan repo użyty jako wejście digestu (jsonb) – do debugowania priorytetów AI.</summary>
public sealed class DigestRepositorySummary
{
    public DigestRepositorySummary(
        string repoFullName,
        int openPrCount,
        int openIssueCount,
        CiStatus ciStatus,
        DateTimeOffset? lastActivityAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);

        RepoFullName = repoFullName;
        OpenPrCount = openPrCount;
        OpenIssueCount = openIssueCount;
        CiStatus = ciStatus;
        LastActivityAt = lastActivityAt;
    }

    public string RepoFullName { get; private set; }

    public int OpenPrCount { get; private set; }

    public int OpenIssueCount { get; private set; }

    public CiStatus CiStatus { get; private set; }

    public DateTimeOffset? LastActivityAt { get; private set; }
}
