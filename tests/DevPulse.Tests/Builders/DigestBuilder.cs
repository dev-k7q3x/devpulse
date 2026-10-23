using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Domain.ValueObjects;

namespace DevPulse.Tests.Builders;

public sealed class DigestBuilder
{
    private Guid _userId = Guid.CreateVersion7();
    private DateOnly _digestDate = DateOnly.FromDateTime(TestClock.Now.UtcDateTime);
    private string _timeZoneId = "Europe/Warsaw";
    private DigestStatus _status = DigestStatus.Sent;
    private DigestAiUsage? _aiUsage = new("claude-sonnet-5-5", 850, 120);
    private List<DigestPriority> _priorities =
    [
        new("octocat/hello-world", "CI na main nie przechodzi od wczoraj – napraw build.", PrioritySeverity.High),
        new("octocat/hello-world", "PR #41 czeka na review od 9 dni.", PrioritySeverity.Medium),
    ];
    private DateTimeOffset _now = TestClock.Now;

    public DigestBuilder ForUser(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public DigestBuilder ForDate(DateOnly digestDate)
    {
        _digestDate = digestDate;
        return this;
    }

    public DigestBuilder WithPriorities(IEnumerable<DigestPriority> priorities)
    {
        _priorities = priorities.ToList();
        return this;
    }

    public DigestBuilder WithoutAi()
    {
        _aiUsage = null;
        return this;
    }

    public DigestBuilder Pending()
    {
        _status = DigestStatus.Pending;
        return this;
    }

    public DigestBuilder Failed()
    {
        _status = DigestStatus.Failed;
        return this;
    }

    public DigestBuilder At(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    public Digest Build()
    {
        var digest = Digest.CreatePending(_userId, _digestDate, _timeZoneId, _now);
        if (_status == DigestStatus.Pending)
        {
            return digest;
        }

        digest.RecordContent(
            $"DevPulse – {_digestDate:yyyy-MM-dd}",
            "octocat@example.com",
            _priorities,
            [new DigestRepositorySummary("octocat/hello-world", 2, 5, CiStatus.Failure, _now.AddHours(-3))],
            "<html><body><h1>DevPulse</h1></body></html>",
            _aiUsage);

        if (_status == DigestStatus.Sent)
        {
            digest.MarkSent("sendgrid-message-id", _now);
        }
        else
        {
            digest.MarkFailed("SendGrid 503", _now);
        }

        return digest;
    }
}
