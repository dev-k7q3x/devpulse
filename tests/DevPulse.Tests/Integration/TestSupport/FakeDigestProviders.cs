using System.Collections.Concurrent;
using System.Net;
using DevPulse.Application.Digests;
using DevPulse.Application.Interfaces;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Domain.ValueObjects;
using DevPulse.Infrastructure.Email;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>Zapisuje wysłane maile zamiast wołać SendGrid; kolejne wywołania mogą rzucać zadane wyjątki.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    private readonly Queue<Exception> _failures = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    /// <summary>Wywoływane po przyjęciu maila – np. anulowanie tokenu albo równoległa instancja joba „w trakcie wysyłki”.</summary>
    public Func<Task>? AfterSend { get; set; }

    public void FailNext(Exception exception) => _failures.Enqueue(exception);

    public static Exception TransientFailure() => new EmailDeliveryException(HttpStatusCode.ServiceUnavailable);

    public static Exception PermanentFailure() => new EmailDeliveryException(HttpStatusCode.BadRequest);

    public static Exception NotConfigured() => new InvalidOperationException("Brak SendGrid:ApiKey.");

    public async Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (_failures.TryDequeue(out var failure))
        {
            throw failure;
        }

        _sent.Enqueue(message);
        var messageId = $"msg-{_sent.Count}";
        if (AfterSend is { } afterSend)
        {
            AfterSend = null;
            await afterSend();
        }

        return messageId;
    }
}

/// <summary>Priorytety AI bez Anthropic: stały wynik albo <c>null</c> (fallback regułowy). Liczy wywołania.</summary>
public sealed class FakeAiPrioritizer : IAiPrioritizer
{
    public const string Model = "claude-sonnet-5-5";

    private int _calls;

    public bool ReturnNull { get; set; }

    public int Calls => _calls;

    public Task<AiPrioritizationResult?> PrioritizeAsync(IReadOnlyList<RepositoryDigestState> repositories, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (ReturnNull || repositories.Count == 0)
        {
            return Task.FromResult<AiPrioritizationResult?>(null);
        }

        var priorities = new List<DigestPriority>
        {
            new(repositories[0].RepoFullName, "Priorytet z AI.", PrioritySeverity.Medium),
        };
        return Task.FromResult<AiPrioritizationResult?>(new AiPrioritizationResult(priorities, new DigestAiUsage(Model, 120, 40)));
    }
}
