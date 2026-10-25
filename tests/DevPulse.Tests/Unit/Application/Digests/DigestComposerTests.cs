using DevPulse.Application.Digests;
using DevPulse.Domain.Enums;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DevPulse.Tests.Unit.Application.Digests;

[Trait("Category", "Unit")]
public class DigestComposerTests
{
    private static readonly DateOnly DigestDate = new(2026, 10, 21);

    private readonly DigestComposer _composer = new(Options.Create(new DigestRulesOptions()));

    [Fact]
    public void Should_OrderPrioritiesFailingCiThenStalePullRequestsThenInactivity_When_AllRulesMatch()
    {
        // Arrange – kolejność wejścia celowo odwrotna do kolejności reguł
        var repositories = new[]
        {
            new RepositoryDigestStateBuilder().Named("acme/abandoned").LastActiveDaysAgo(45).Build(),
            new RepositoryDigestStateBuilder().Named("acme/waiting").WithOpenPullRequests(3, oldestAgeDays: 12).Build(),
            new RepositoryDigestStateBuilder().Named("acme/broken").WithCi(CiStatus.Failure).Build(),
        };

        // Act
        var composition = _composer.Compose(repositories, DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Select(p => (p.RepoFullName, p.Severity)).Should().Equal(
            ("acme/broken", PrioritySeverity.High),
            ("acme/waiting", PrioritySeverity.Medium),
            ("acme/abandoned", PrioritySeverity.Low));
    }

    [Fact]
    public void Should_LimitToFivePriorities_When_MoreRepositoriesNeedAttention()
    {
        // Arrange
        var repositories = Enumerable.Range(1, 8)
            .Select(i => new RepositoryDigestStateBuilder().Named($"acme/repo-{i}").WithCi(CiStatus.Failure).Build())
            .ToList();

        // Act
        var composition = _composer.Compose(repositories, DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Should().HaveCount(5);
        composition.RepositorySummary.Should().HaveCount(8);
    }

    [Fact]
    public void Should_UseOnlyHighestRulePerRepository_When_RepositoryMatchesSeveralRules()
    {
        // Arrange – failing CI, stary PR i brak aktywności w jednym repo
        var repository = new RepositoryDigestStateBuilder()
            .Named("acme/everything-wrong")
            .WithCi(CiStatus.Failure)
            .WithOpenPullRequests(2, oldestAgeDays: 30)
            .LastActiveDaysAgo(60)
            .Build();

        // Act
        var composition = _composer.Compose([repository], DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Should().ContainSingle()
            .Which.Severity.Should().Be(PrioritySeverity.High);
    }

    [Fact]
    public void Should_RankFailingCiByOpenPullRequests_When_SeveralBuildsFail()
    {
        // Arrange – więcej otwartych PR-ów = więcej zablokowanej pracy
        var repositories = new[]
        {
            new RepositoryDigestStateBuilder().Named("acme/quiet").WithCi(CiStatus.Failure).Build(),
            new RepositoryDigestStateBuilder().Named("acme/busy").WithCi(CiStatus.Failure).WithOpenPullRequests(9, oldestAgeDays: 1).Build(),
        };

        // Act
        var composition = _composer.Compose(repositories, DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Select(p => p.RepoFullName).Should().Equal("acme/busy", "acme/quiet");
    }

    [Fact]
    public void Should_RankStalePullRequestsByAge_When_SeveralWait()
    {
        // Arrange
        var repositories = new[]
        {
            new RepositoryDigestStateBuilder().Named("acme/week").WithOpenPullRequests(1, oldestAgeDays: 8).Build(),
            new RepositoryDigestStateBuilder().Named("acme/month").WithOpenPullRequests(1, oldestAgeDays: 31).Build(),
        };

        // Act
        var composition = _composer.Compose(repositories, DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Select(p => p.RepoFullName).Should().Equal("acme/month", "acme/week");
        composition.FallbackPriorities[0].Text.Should().Contain("31 dni").And.Contain("1 otwarty PR");
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void Should_ApplyStalePullRequestThreshold_When_PullRequestAgeAtBoundary(int ageDays, bool expectedPriority)
    {
        // Arrange – próg domyślny: 7 dni
        var repository = new RepositoryDigestStateBuilder().WithOpenPullRequests(1, ageDays).Build();

        // Act
        var composition = _composer.Compose([repository], DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Any().Should().Be(expectedPriority);
    }

    [Fact]
    public void Should_NotTreatMissingActivityDateAsInactivity_When_SnapshotHasNoData()
    {
        // Arrange – brak daty to brak danych, nie porzucone repo
        var repository = new RepositoryDigestStateBuilder().WithoutActivityData().Build();

        // Act
        var composition = _composer.Compose([repository], DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Should().BeEmpty();
    }

    [Fact]
    public void Should_UseConfiguredThresholds_When_OptionsChanged()
    {
        // Arrange
        var composer = new DigestComposer(Options.Create(new DigestRulesOptions { InactiveAfter = TimeSpan.FromDays(3) }));
        var repository = new RepositoryDigestStateBuilder().LastActiveDaysAgo(4).Build();

        // Act
        var composition = composer.Compose([repository], DigestDate, TestClock.Now);

        // Assert
        composition.FallbackPriorities.Should().ContainSingle()
            .Which.Text.Should().Be("Brak aktywności od 4 dni – sprawdź, czy repozytorium nadal wymaga śledzenia.");
    }

    [Fact]
    public void Should_SummarizeRepositoriesSortedByName_When_Composed()
    {
        // Arrange
        var repositories = new[]
        {
            new RepositoryDigestStateBuilder().Named("zeta/api").WithIssues(4).Build(),
            new RepositoryDigestStateBuilder().Named("Alpha/web").WithCi(CiStatus.Pending).Build(),
        };

        // Act
        var composition = _composer.Compose(repositories, DigestDate, TestClock.Now);

        // Assert
        composition.RepositorySummary.Select(s => s.RepoFullName).Should().Equal("Alpha/web", "zeta/api");
        composition.RepositorySummary[1].OpenIssueCount.Should().Be(4);
        composition.RepositorySummary[0].CiStatus.Should().Be(CiStatus.Pending);
    }

    [Theory]
    [MemberData(nameof(SubjectCases))]
    public void Should_DescribeOverallStateInSubject_When_Composed(int healthy, int failing, string expectedSubject)
    {
        // Arrange
        var repositories = Enumerable.Range(1, healthy)
            .Select(i => new RepositoryDigestStateBuilder().Named($"acme/ok-{i}").Build())
            .Concat(Enumerable.Range(1, failing).Select(i => new RepositoryDigestStateBuilder().Named($"acme/bad-{i}").WithCi(CiStatus.Failure).Build()))
            .ToList();

        // Act
        var composition = _composer.Compose(repositories, DigestDate, TestClock.Now);

        // Assert
        composition.Subject.Should().Be(expectedSubject);
    }

    public static TheoryData<int, int, string> SubjectCases() => new()
    {
        { 0, 0, "DevPulse – 21.10.2026: brak śledzonych repozytoriów" },
        { 3, 0, "DevPulse – 21.10.2026: wszystkie repozytoria w porządku" },
        { 0, 1, "DevPulse – 21.10.2026: uwagi wymaga 1 z 1 repozytorium" },
        { 9, 2, "DevPulse – 21.10.2026: uwagi wymaga 2 z 11 repozytoriów" },
    };
}
