using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Domain.ValueObjects;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class DigestTests
{
    private static readonly DigestRepositorySummary[] Summary =
        [new("octocat/hello-world", 1, 2, CiStatus.Success, TestClock.Now)];

    [Fact]
    public void Should_StartAsPendingWithoutContent_When_Created()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var date = new DateOnly(2026, 10, 21);

        // Act
        var digest = Digest.CreatePending(userId, date, "Europe/Warsaw", TestClock.Now);

        // Assert
        digest.Status.Should().Be(DigestStatus.Pending);
        digest.UserId.Should().Be(userId);
        digest.DigestDate.Should().Be(date);
        digest.HtmlBody.Should().BeNull();
        digest.Priorities.Should().BeNull();
        digest.AttemptCount.Should().Be(0);
    }

    [Fact]
    public void Should_RecordAiMetadata_When_ContentHasAiUsage()
    {
        // Arrange
        var digest = new DigestBuilder().Pending().Build();

        // Act
        digest.RecordContent("Temat", "a@example.com", [], Summary, "<p>x</p>", new DigestAiUsage("claude-sonnet-5-5", 700, 90));

        // Assert
        digest.AiUsed.Should().BeTrue();
        digest.AiModel.Should().Be("claude-sonnet-5-5");
        digest.AiInputTokens.Should().Be(700);
        digest.AiOutputTokens.Should().Be(90);
        digest.RepositorySummary.Should().ContainSingle();
    }

    [Fact]
    public void Should_MarkAiUnused_When_ContentFromFallback()
    {
        // Arrange
        var digest = new DigestBuilder().Pending().Build();

        // Act
        digest.RecordContent("Temat", "a@example.com", [], Summary, "<p>x</p>", aiUsage: null);

        // Assert
        digest.AiUsed.Should().BeFalse();
        digest.AiModel.Should().BeNull();
    }

    [Fact]
    public void Should_Throw_When_MoreThanFivePriorities()
    {
        // Arrange
        var digest = new DigestBuilder().Pending().Build();
        var priorities = Enumerable.Range(1, Digest.MaxPriorities + 1)
            .Select(n => new DigestPriority("o/r", $"Priorytet {n}", PrioritySeverity.Low))
            .ToList();
        var act = () => digest.RecordContent("Temat", "a@example.com", priorities, Summary, "<p>x</p>", null);

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_SetSentAtAndAttempt_When_MarkedSent()
    {
        // Arrange
        var digest = new DigestBuilder().Pending().Build();
        digest.RecordContent("Temat", "a@example.com", [], Summary, "<p>x</p>", null);
        var sentAt = TestClock.Now.AddMinutes(1);

        // Act
        digest.MarkSent("msg-1", sentAt);

        // Assert
        digest.Status.Should().Be(DigestStatus.Sent);
        digest.SentAt.Should().Be(sentAt);
        digest.AttemptCount.Should().Be(1);
        digest.EmailProviderMessageId.Should().Be("msg-1");
    }

    [Fact]
    public void Should_Throw_When_MarkedSentWithoutContent()
    {
        // Arrange
        var digest = new DigestBuilder().Pending().Build();
        var act = () => digest.MarkSent(null, TestClock.Now);

        // Act & Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_AllowRetryAfterFailure_When_SentLater()
    {
        // Arrange
        var digest = new DigestBuilder().Failed().Build();

        // Act
        digest.MarkSent("msg-2", TestClock.Now.AddMinutes(5));

        // Assert
        digest.Status.Should().Be(DigestStatus.Sent);
        digest.AttemptCount.Should().Be(2);
        digest.Error.Should().BeNull();
    }

    [Fact]
    public void Should_RecordErrorAndAttempt_When_MarkedFailed()
    {
        // Arrange
        var digest = new DigestBuilder().Pending().Build();

        // Act
        digest.MarkFailed(new string('e', 1500), TestClock.Now);

        // Assert
        digest.Status.Should().Be(DigestStatus.Failed);
        digest.AttemptCount.Should().Be(1);
        digest.Error.Should().HaveLength(Digest.ErrorMaxLength);
        digest.SentAt.Should().BeNull();
    }

    [Fact]
    public void Should_Throw_When_FailingAlreadySentDigest()
    {
        // Arrange
        var digest = new DigestBuilder().Build();
        var act = () => digest.MarkFailed("late error", TestClock.Now);

        // Act & Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
