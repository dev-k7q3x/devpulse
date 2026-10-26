using DevPulse.Application.DigestHistory;
using DevPulse.Application.Digests;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>
/// SendDigestJob na prawdziwym PostgreSQL (UNIQUE user_id + digest_date, jsonb) z fake'ami AI, SendGrid i GitHuba –
/// bez sieci i bez serwera Hangfire.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class SendDigestJobTests(DevPulseApiFactory factory)
{
    private static readonly DateTimeOffset Now = TestClock.Now.AddMinutes(5);
    private static readonly DateOnly DigestDate = new(2026, 10, 21);

    private static readonly HashSet<string> KnownErrorCodes =
    [
        DigestErrorCodes.EmailDeliveryFailed,
        DigestErrorCodes.EmailNotConfigured,
        DigestErrorCodes.NoRecipient,
        DigestErrorCodes.ContentFailed,
    ];

    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly RecordingEmailSender _email = new();
    private readonly FakeAiPrioritizer _ai = new();
    private readonly ThrowingGitHubClient _throwingGitHub = new();

    [Fact]
    public async Task Should_SendDigestWithAiPrioritiesAndStoreContent_When_SnapshotsFresh()
    {
        // Arrange
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().ContainSingle().Which.Should().Match<EmailMessage>(m =>
            m.To == "octocat@example.com" && m.HtmlBody.Contains("acme/api") && m.PlainTextBody != null);

        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Sent);
        digest.AttemptCount.Should().Be(1);
        digest.EmailProviderMessageId.Should().Be("msg-1");
        digest.HtmlBody.Should().Be(_email.Sent.Single().HtmlBody, "historia pokazuje dokładnie to, co poszło mailem");
        digest.AiUsed.Should().BeTrue();
        digest.AiModel.Should().Be(FakeAiPrioritizer.Model);
        digest.AiInputTokens.Should().Be(120);
        digest.AiOutputTokens.Should().Be(40);
        digest.Priorities.Should().ContainSingle().Which.Text.Should().Be("Priorytet z AI.");
        digest.RecipientEmail.Should().Be("octocat@example.com");
        digest.SentAt.Should().Be(Now);
        _throwingGitHub.WasCalledFor(user.Id).Should().BeFalse("świeże snapshoty nie wymagają GitHuba");
    }

    [Fact]
    public async Task Should_SendOnlyOneEmail_When_JobRunsTwiceForSameDate()
    {
        // Arrange – np. scheduler uruchomiony dwa razy albo ręczne ponowienie z /hangfire
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        var job = CreateJob();

        // Act
        await job.SendAsync(user.Id, DigestDate, CancellationToken.None);
        await job.SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().ContainSingle();
        _ai.Calls.Should().Be(1);
        await using var db = factory.CreateDbContext();
        (await db.Digests.CountAsync(d => d.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Should_UseRuleBasedPriorities_When_AiFails()
    {
        // Arrange – AI zwraca null (błąd, timeout, zły JSON); repo z czerwonym CI
        _ai.ReturnNull = true;
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api", CiStatus.Failure);

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Sent);
        digest.AiUsed.Should().BeFalse();
        digest.AiModel.Should().BeNull();
        digest.Priorities.Should().ContainSingle()
            .Which.Should().Match<DigestPriority>(p => p.RepoFullName == "acme/api" && p.Severity == PrioritySeverity.High);
    }

    [Fact]
    public async Task Should_MarkFailedAndRetryOnSameRowWithoutSecondAiCall_When_DeliveryFailsTransiently()
    {
        // Arrange – SendGrid 503 przy pierwszej próbie
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        _email.FailNext(RecordingEmailSender.TransientFailure());
        var job = CreateJob();

        // Act
        var firstAttempt = () => job.SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert – wyjątek uruchamia ponowienie Hangfire, wiersz zostaje jako Failed z kodem
        await firstAttempt.Should().ThrowAsync<EmailDeliveryException>();
        var failed = await SingleDigestAsync(user.Id);
        failed.Status.Should().Be(DigestStatus.Failed);
        failed.Error.Should().Be(DigestErrorCodes.EmailDeliveryFailed);
        failed.AttemptCount.Should().Be(1);

        // Act – ponowienie
        await job.SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        var sent = await SingleDigestAsync(user.Id);
        sent.Id.Should().Be(failed.Id);
        sent.Status.Should().Be(DigestStatus.Sent);
        sent.Error.Should().BeNull();
        sent.AttemptCount.Should().Be(2);
        _email.Sent.Should().ContainSingle().Which.HtmlBody.Should().Be(sent.HtmlBody);
        _ai.Calls.Should().Be(1, "ponowienie wysyła zapisaną treść – AI najwyżej raz na dobę");
    }

    [Fact]
    public async Task Should_MarkFailedWithoutRetry_When_ProviderRejectsPermanently()
    {
        // Arrange – 4xx: ponowienie nic nie zmieni
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        _email.FailNext(RecordingEmailSender.PermanentFailure());

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Failed);
        digest.Error.Should().Be(DigestErrorCodes.EmailDeliveryFailed);
    }

    [Fact]
    public async Task Should_RecordNotConfiguredWithoutRetry_When_SenderHasNoConfiguration()
    {
        // Arrange – brak SendGrid:ApiKey / FromEmail
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        _email.FailNext(RecordingEmailSender.NotConfigured());

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Failed);
        digest.Error.Should().Be(DigestErrorCodes.EmailNotConfigured);
        digest.Error.Should().NotContain("SendGrid:ApiKey", "do historii trafia kod, nie treść wyjątku");
    }

    [Fact]
    public async Task Should_RecordNoRecipientWithoutSending_When_EmailNotConfirmed()
    {
        // Arrange
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api", email: null);

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().BeEmpty();
        _ai.Calls.Should().Be(0);
        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Failed);
        digest.Error.Should().Be(DigestErrorCodes.NoRecipient);
    }

    [Fact]
    public async Task Should_SkipWithoutCreatingDigest_When_DigestDisabled()
    {
        // Arrange – użytkownik wyłączył digest między schedulerem a wykonaniem joba
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api", digestEnabled: false);

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().BeEmpty();
        await using var db = factory.CreateDbContext();
        (await db.Digests.AnyAsync(d => d.UserId == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Should_RefreshSnapshotsBeforeComposing_When_SnapshotStale()
    {
        // Arrange – snapshot sprzed godziny, interwał odświeżania 15 min
        var (user, repository) = await SeedUserWithRepositoryAsync("acme/api", snapshotAt: Now.AddHours(-1));
        var gitHub = new StateGitHubClient
        {
            States = ids => ids.Select(id => new GitHubRepositoryStateResult(
                id, StateGitHubClient.State(id, owner: "acme", name: "api", openPrCount: 11), null)).ToList(),
        };

        // Act
        await CreateJob(gitHub).SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        gitHub.RequestedNodeIds.Should().ContainSingle().Which.Should().Equal(repository.GitHubNodeId);
        var digest = await SingleDigestAsync(user.Id);
        digest.RepositorySummary.Should().ContainSingle().Which.OpenPrCount.Should().Be(11, "digest powstaje ze świeżych danych");
    }

    [Fact]
    public async Task Should_ContainOnlyOwnRepositories_When_OtherUserTracksRepositories()
    {
        // Arrange
        var (userA, _) = await SeedUserWithRepositoryAsync("acme/mine");
        await SeedUserWithRepositoryAsync("other/secret");

        // Act
        await CreateJob().SendAsync(userA.Id, DigestDate, CancellationToken.None);

        // Assert
        var digest = await SingleDigestAsync(userA.Id);
        digest.RepositorySummary!.Select(r => r.RepoFullName).Should().Equal("acme/mine");
        _email.Sent.Single().HtmlBody.Should().NotContain("other/secret");
    }

    [Fact]
    public async Task Should_NotSend_When_DigestAlreadyInProgress()
    {
        // Arrange – świeży Pending: inna instancja joba właśnie wysyła
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        await SeedAsync(new DigestBuilder().ForUser(user.Id).ForDate(DigestDate).Pending().At(Now.AddMinutes(-2)).Build());

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().BeEmpty();
        (await SingleDigestAsync(user.Id)).Status.Should().Be(DigestStatus.Pending);
    }

    [Fact]
    public async Task Should_TakeOverAbandonedAttempt_When_PendingOlderThanThreshold()
    {
        // Arrange – Pending sprzed 2 h: proces padł w trakcie wysyłki
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        await SeedAsync(new DigestBuilder().ForUser(user.Id).ForDate(DigestDate).Pending().At(Now.AddHours(-2)).Build());

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().ContainSingle();
        (await SingleDigestAsync(user.Id)).Status.Should().Be(DigestStatus.Sent);
    }

    [Fact]
    public async Task Should_TakeOverAbandonedAttempt_When_HangfireRequeuesAfterInvisibilityTimeout()
    {
        // Arrange – Hangfire.PostgreSql oddaje przerwany job po 30 min; próg porzucenia musi być krótszy
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        await SeedAsync(new DigestBuilder().ForUser(user.Id).ForDate(DigestDate).Pending().At(Now.AddMinutes(-30)).Build());

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().ContainSingle();
        (await SingleDigestAsync(user.Id)).Status.Should().Be(DigestStatus.Sent);
    }

    [Fact]
    public async Task Should_RecordContentFailedWithoutRetry_When_SnapshotRefreshTimesOut()
    {
        // Arrange – timeout HTTP przy odświeżaniu (TaskCanceledException, job nie został anulowany)
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api", snapshotAt: Now.AddHours(-1));

        // Act
        var act = () => CreateJob(new TimingOutGitHubClient()).SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _email.Sent.Should().BeEmpty();
        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Failed);
        digest.Error.Should().Be(DigestErrorCodes.ContentFailed);
    }

    [Fact]
    public async Task Should_PersistSent_When_ServerStopsRightAfterEmailAccepted()
    {
        // Arrange – SendGrid przyjął mail, a zaraz potem Hangfire anuluje job (zatrzymanie serwera)
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        using var cancellation = new CancellationTokenSource();
        _email.AfterSend = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        var job = CreateJob();

        // Act
        await job.SendAsync(user.Id, DigestDate, cancellation.Token);
        await job.SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert – status zapisany mimo anulowania, więc ani ponowienie, ani odzyskiwanie nie wyśle drugi raz
        (await SingleDigestAsync(user.Id)).Status.Should().Be(DigestStatus.Sent);
        _email.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_NotTakeOverAgain_When_SecondInstanceRunsDuringTakenOverSend()
    {
        // Arrange – porzucony Pending; w trakcie wysyłki przez instancję A startuje instancja B (np. odzyskiwanie ze schedulera)
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        await SeedAsync(new DigestBuilder().ForUser(user.Id).ForDate(DigestDate).Pending().At(Now.AddMinutes(-40)).Build());
        var job = CreateJob();
        _email.AfterSend = () => job.SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Act
        await job.SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert – przejęcie odświeżyło created_at, więc B uznało wiersz za „w trakcie” i nic nie wysłało
        _email.Sent.Should().ContainSingle();
        var digest = await SingleDigestAsync(user.Id);
        digest.Status.Should().Be(DigestStatus.Sent);
        digest.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Should_NotSendAgain_When_DigestAlreadySent()
    {
        // Arrange
        var (user, _) = await SeedUserWithRepositoryAsync("acme/api");
        await SeedAsync(new DigestBuilder().ForUser(user.Id).ForDate(DigestDate).At(Now.AddHours(-1)).Build());

        // Act
        await CreateJob().SendAsync(user.Id, DigestDate, CancellationToken.None);

        // Assert
        _email.Sent.Should().BeEmpty();
        _ai.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Should_StoreOnlyKnownErrorCodes_When_AnyFailureOccurs()
    {
        // Arrange – trzy różne awarie u trzech użytkowników
        var (transient, _) = await SeedUserWithRepositoryAsync("acme/a");
        var (permanent, _) = await SeedUserWithRepositoryAsync("acme/b");
        var (noRecipient, _) = await SeedUserWithRepositoryAsync("acme/c", email: null);

        // Act
        _email.FailNext(RecordingEmailSender.TransientFailure());
        await FluentActions.Awaiting(() => CreateJob().SendAsync(transient.Id, DigestDate, CancellationToken.None))
            .Should().ThrowAsync<EmailDeliveryException>();
        _email.FailNext(RecordingEmailSender.PermanentFailure());
        await CreateJob().SendAsync(permanent.Id, DigestDate, CancellationToken.None);
        await CreateJob().SendAsync(noRecipient.Id, DigestDate, CancellationToken.None);

        // Assert
        await using var db = factory.CreateDbContext();
        var errors = await db.Digests
            .Where(d => d.UserId == transient.Id || d.UserId == permanent.Id || d.UserId == noRecipient.Id)
            .Select(d => d.Error)
            .ToListAsync();
        errors.Should().HaveCount(3).And.OnlyContain(e => e != null && KnownErrorCodes.Contains(e));
    }

    private SendDigestJob CreateJob(IGitHubClient? gitHub = null)
    {
        var dbContextFactory = factory.Services.GetRequiredService<IDbContextFactory<DevPulseDbContext>>();
        var snapshotOptions = Options.Create(new SnapshotOptions());
        var snapshotRefreshJob = new SnapshotRefreshJob(
            dbContextFactory,
            gitHub ?? _throwingGitHub,
            new RecordingBackgroundJobClient(),
            _timeProvider,
            snapshotOptions,
            NullLogger<SnapshotRefreshJob>.Instance);

        return new SendDigestJob(
            dbContextFactory,
            snapshotRefreshJob,
            new DigestComposer(Options.Create(new DigestRulesOptions())),
            _ai,
            new DigestEmailRenderer(),
            _email,
            _timeProvider,
            snapshotOptions,
            Options.Create(new DigestPipelineOptions()),
            Options.Create(new AppOptions { PublicBaseUrl = "https://devpulse.example" }),
            NullLogger<SendDigestJob>.Instance);
    }

    private async Task<(User User, TrackedRepository Repository)> SeedUserWithRepositoryAsync(
        string fullName,
        CiStatus ciStatus = CiStatus.Success,
        string? email = "octocat@example.com",
        bool digestEnabled = true,
        DateTimeOffset? snapshotAt = null)
    {
        var user = new UserBuilder().Build();
        var parts = fullName.Split('/');
        var repository = new TrackedRepositoryBuilder().ForUser(user.Id).WithFullName(parts[0], parts[1]).Build();
        await SeedAsync(
            user,
            new UserSettingsBuilder().ForUser(user.Id).WithEmail(email).WithSchedule(7, "UTC", digestEnabled).Build(),
            repository,
            new RepositorySnapshotBuilder().ForRepository(repository.Id).WithCi(ciStatus).At(snapshotAt ?? Now.AddMinutes(-5)).Build());
        return (user, repository);
    }

    private async Task<Digest> SingleDigestAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext();
        return await db.Digests.AsNoTracking().SingleAsync(d => d.UserId == userId);
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    /// <summary>GraphQL nie odpowiada w czasie – HttpClient rzuca TaskCanceledException.</summary>
    private sealed class TimingOutGitHubClient : IGitHubClient
    {
        public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new TaskCanceledException();

        public Task<GitHubRepository?> GetRepositoryAsync(Guid userId, long gitHubRepoId, CancellationToken cancellationToken) =>
            throw new TaskCanceledException();

        public Task<IReadOnlyList<GitHubRepositoryStateResult>> GetRepositoryStatesAsync(
            Guid userId,
            IReadOnlyList<string> nodeIds,
            CancellationToken cancellationToken) =>
            throw new TaskCanceledException();
    }
}
