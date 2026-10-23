using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class TrackedRepositoryTests
{
    [Fact]
    public void Should_SetIdentityAndNameParts_When_Created()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var repository = TrackedRepository.Create(
            userId,
            1296269,
            "R_kgDOABC",
            new RepoFullName("octocat", "hello-world"),
            "https://github.com/octocat/hello-world",
            "main",
            isPrivate: true,
            TestClock.Now);

        // Assert
        repository.Id.Version.Should().Be(7);
        repository.UserId.Should().Be(userId);
        repository.GitHubRepoId.Should().Be(1296269);
        repository.Owner.Should().Be("octocat");
        repository.Name.Should().Be("hello-world");
        repository.FullName.Should().Be("octocat/hello-world");
        repository.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public void Should_Throw_When_GitHubRepoIdNotPositive()
    {
        // Arrange
        var act = () => TrackedRepository.Create(
            Guid.CreateVersion7(),
            0,
            "R_kgDOABC",
            new RepoFullName("octocat", "hello-world"),
            "https://github.com/octocat/hello-world",
            "main",
            false,
            TestClock.Now);

        // Act & Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_KeepGitHubIdsAndUpdateName_When_RepositoryRenamed()
    {
        // Arrange
        var repository = new TrackedRepositoryBuilder().Build();
        var gitHubRepoId = repository.GitHubRepoId;
        var later = TestClock.Now.AddDays(1);

        // Act
        repository.UpdateMetadata(new RepoFullName("new-org", "renamed"), "https://github.com/new-org/renamed", "trunk", true, later);

        // Assert
        repository.GitHubRepoId.Should().Be(gitHubRepoId);
        repository.FullName.Should().Be("new-org/renamed");
        repository.DefaultBranch.Should().Be("trunk");
        repository.UpdatedAt.Should().Be(later);
        repository.CreatedAt.Should().Be(TestClock.Now);
    }
}
