using DevPulse.Domain.Enums;
using DevPulse.Domain.ValueObjects;

namespace DevPulse.Domain.Entities;

/// <summary>
/// Historia digestów (retencja 30 dni). Idempotencja: UNIQUE (user_id, digest_date) – job najpierw wstawia <see cref="DigestStatus.Pending"/>,
/// a unique violation oznacza, że digest na ten dzień już istnieje.
/// </summary>
public sealed class Digest
{
    public const int MaxPriorities = 5;
    public const int TimeZoneIdMaxLength = 64;
    public const int SubjectMaxLength = 200;
    public const int RecipientEmailMaxLength = 254;
    public const int EmailProviderMessageIdMaxLength = 128;
    public const int ErrorMaxLength = 1000;

    private Digest()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Data lokalna użytkownika, dla której digest jest generowany.</summary>
    public DateOnly DigestDate { get; private set; }

    /// <summary>Strefa w chwili generowania – do wyświetlenia, jeśli użytkownik później ją zmieni.</summary>
    public string TimeZoneId { get; private set; } = string.Empty;

    public DigestStatus Status { get; private set; }

    public string? Subject { get; private set; }

    public string? RecipientEmail { get; private set; }

    public List<DigestPriority>? Priorities { get; private set; }

    public List<DigestRepositorySummary>? RepositorySummary { get; private set; }

    /// <summary>Wyrenderowany HTML dokładnie taki, jak w mailu – źródło prawdy dla podglądu.</summary>
    public string? HtmlBody { get; private set; }

    public bool AiUsed { get; private set; }

    public string? AiModel { get; private set; }

    public int? AiInputTokens { get; private set; }

    public int? AiOutputTokens { get; private set; }

    /// <summary><c>X-Message-Id</c> z SendGrid.</summary>
    public string? EmailProviderMessageId { get; private set; }

    public short AttemptCount { get; private set; }

    /// <summary>Ostatni błąd, bez danych wrażliwych.</summary>
    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static Digest CreatePending(Guid userId, DateOnly digestDate, string timeZoneId, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Brak id użytkownika.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (timeZoneId.Length > TimeZoneIdMaxLength)
        {
            throw new ArgumentException($"Id strefy ma maksymalnie {TimeZoneIdMaxLength} znaków.", nameof(timeZoneId));
        }

        return new Digest
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DigestDate = digestDate,
            TimeZoneId = timeZoneId,
            Status = DigestStatus.Pending,
            CreatedAt = Guard.Utc(now, nameof(now)),
        };
    }

    /// <param name="subject">Temat maila.</param>
    /// <param name="recipientEmail">Adres, na który idzie wysyłka.</param>
    /// <param name="priorities">Maks. <see cref="MaxPriorities"/> priorytetów.</param>
    /// <param name="repositorySummary">Stan repo użyty jako wejście.</param>
    /// <param name="htmlBody">Wyrenderowany HTML maila.</param>
    /// <param name="aiUsage">Metadane AI; <c>null</c> = priorytety z fallbacku regułowego.</param>
    public void RecordContent(
        string subject,
        string recipientEmail,
        IReadOnlyCollection<DigestPriority> priorities,
        IEnumerable<DigestRepositorySummary> repositorySummary,
        string htmlBody,
        DigestAiUsage? aiUsage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
        ArgumentNullException.ThrowIfNull(priorities);
        ArgumentNullException.ThrowIfNull(repositorySummary);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);
        EnsureNotSent();

        if (priorities.Count > MaxPriorities)
        {
            throw new ArgumentException($"Digest ma maksymalnie {MaxPriorities} priorytetów.", nameof(priorities));
        }

        if (recipientEmail.Length > RecipientEmailMaxLength)
        {
            throw new ArgumentException($"Adres email ma maksymalnie {RecipientEmailMaxLength} znaków.", nameof(recipientEmail));
        }

        Subject = Guard.Truncate(subject, SubjectMaxLength);
        RecipientEmail = recipientEmail;
        Priorities = [.. priorities];
        RepositorySummary = [.. repositorySummary];
        HtmlBody = htmlBody;
        AiUsed = aiUsage is not null;
        AiModel = aiUsage?.Model;
        AiInputTokens = aiUsage?.InputTokens;
        AiOutputTokens = aiUsage?.OutputTokens;
    }

    public void MarkSent(string? emailProviderMessageId, DateTimeOffset now)
    {
        EnsureNotSent();
        if (HtmlBody is null)
        {
            throw new InvalidOperationException("Nie można oznaczyć jako wysłany digestu bez treści.");
        }

        AttemptCount++;
        Status = DigestStatus.Sent;
        SentAt = Guard.Utc(now, nameof(now));
        EmailProviderMessageId = Guard.Truncate(emailProviderMessageId, EmailProviderMessageIdMaxLength);
        Error = null;
    }

    /// <summary>Nieudana próba – retry Hangfire może później wywołać <see cref="MarkSent"/>.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Guard.Utc(now, nameof(now));
        EnsureNotSent();

        AttemptCount++;
        Status = DigestStatus.Failed;
        Error = Guard.Truncate(error, ErrorMaxLength);
    }

    private void EnsureNotSent()
    {
        if (Status == DigestStatus.Sent)
        {
            throw new InvalidOperationException("Digest został już wysłany.");
        }
    }
}
