using DevPulse.Domain.Enums;

namespace DevPulse.Application.DTOs.Digests;

/// <summary>Element listy <c>GET /api/digests</c> – projekcja bez <c>html_body</c> i <c>repository_summary</c>.</summary>
public sealed record DigestListItem(
    Guid Id,
    DateOnly DigestDate,
    DigestStatus Status,
    string? Subject,
    DateTimeOffset? SentAt,
    bool AiUsed,
    IReadOnlyList<DigestPriorityItem> Priorities);

public sealed record DigestPriorityItem(string RepoFullName, string Text, PrioritySeverity Severity);

/// <summary>
/// <c>GET /api/digests/{id}</c>. Pola monitoringu (tokeny AI, model, id wiadomości SendGrid) celowo nie są wystawiane.
/// </summary>
public sealed record DigestDetailResponse(
    Guid Id,
    DateOnly DigestDate,
    DigestStatus Status,
    string? Subject,
    DateTimeOffset? SentAt,
    bool AiUsed,
    IReadOnlyList<DigestPriorityItem> Priorities,
    string TimeZoneId,
    string? RecipientEmail,
    int AttemptCount,
    string? Error,
    bool HasHtml);
