using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class RepositorySnapshotTests
{
    [Fact]
    public void Should_KeepOnlyTopTenPullRequests_When_MoreProvided()
    {
        // Arrange
        var pullRequests = Enumerable.Range(1, 15)
            .Select(n => new OpenPullRequest(n, $"PR {n}", $"https://github.com/o/r/pull/{n}", "dev", TestClock.Now, false));

        // Act
        var snapshot = RepositorySnapshot.Create(Guid.CreateVersion7(), 15, 0, pullRequests, CiStatus.None, null, null, TestClock.Now);

        // Assert
        snapshot.OpenPrCount.Should().Be(15);
        snapshot.OpenPullRequests.Should().HaveCount(RepositorySnapshot.MaxOpenPullRequests);
        snapshot.OpenPullRequests[0].Number.Should().Be(1);
        snapshot.RefreshedAt.Should().Be(TestClock.Now);
        snapshot.LastAttemptAt.Should().Be(TestClock.Now);
        snapshot.LastError.Should().BeNull();
    }

    [Fact]
    public void Should_Throw_When_CommitShaHasWrongLength()
    {
        // Arrange
        var act = () => RepositorySnapshot.Create(Guid.CreateVersion7(), 0, 0, [], CiStatus.Success, "abc123", null, TestClock.Now);

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_CountNegative()
    {
        // Arrange
        var act = () => RepositorySnapshot.Create(Guid.CreateVersion7(), -1, 0, [], CiStatus.None, null, null, TestClock.Now);

        // Act & Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_KeepPreviousDataAndRecordError_When_RefreshFails()
    {
        // Arrange
        var snapshot = new RepositorySnapshotBuilder().WithCounts(3, 7).Build();
        var later = TestClock.Now.AddMinutes(15);

        // Act
        snapshot.RecordFailure("NotFound", later);

        // Assert
        snapshot.OpenPrCount.Should().Be(3);
        snapshot.OpenIssueCount.Should().Be(7);
        snapshot.RefreshedAt.Should().Be(TestClock.Now);
        snapshot.LastAttemptAt.Should().Be(later);
        snapshot.LastError.Should().Be("NotFound");
    }

    [Fact]
    public void Should_TruncateError_When_LongerThanColumn()
    {
        // Arrange
        var snapshot = new RepositorySnapshotBuilder().Build();

        // Act
        snapshot.RecordFailure(new string('e', 800), TestClock.Now);

        // Assert
        snapshot.LastError.Should().HaveLength(RepositorySnapshot.LastErrorMaxLength);
    }

    [Fact]
    public void Should_ClearError_When_RefreshSucceedsAfterFailure()
    {
        // Arrange
        var snapshot = new RepositorySnapshotBuilder().WithLastError("Forbidden").Build();
        var later = TestClock.Now.AddMinutes(30);

        // Act
        snapshot.RecordSuccess(1, 2, [], CiStatus.Failure, null, later.AddHours(-1), later);

        // Assert
        snapshot.LastError.Should().BeNull();
        snapshot.CiStatus.Should().Be(CiStatus.Failure);
        snapshot.RefreshedAt.Should().Be(later);
        snapshot.OpenPullRequests.Should().BeEmpty();
    }
}
