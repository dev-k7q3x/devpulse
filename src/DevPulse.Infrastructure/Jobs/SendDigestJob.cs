using DevPulse.Application.DigestHistory;
using DevPulse.Application.Digests;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DevPulse.Infrastructure.Jobs;

/// <summary>
/// M9: jeden digest jednego użytkownika na jedną lokalną datę. Idempotencja przez UNIQUE (user_id, digest_date):
/// wstawienie <c>Pending</c> = „mój digest”, unique violation = digest już istnieje (wysłany, w trakcie albo do ponowienia).
/// Przebieg: odświeżenie snapshotów, jeśli są stare → composer → AI (null = fallback regułowy) → render → SendGrid.
/// Jedyny wywołujący Anthropic (≤ 1×/użytkownik/dobę); ponowienie wysyłki używa zapisanej treści, bez drugiego wywołania AI.
/// W <c>digests.error</c> trafiają wyłącznie kody z <see cref="DigestErrorCodes"/> – pole widzi użytkownik.
/// </summary>
public sealed class SendDigestJob(
    IDbContextFactory<DevPulseDbContext> dbContextFactory,
    SnapshotRefreshJob snapshotRefreshJob,
    IDigestComposer composer,
    IAiPrioritizer aiPrioritizer,
    IDigestEmailRenderer renderer,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    IOptions<SnapshotOptions> snapshotOptions,
    IOptions<DigestPipelineOptions> pipelineOptions,
    IOptions<AppOptions> appOptions,
    ILogger<SendDigestJob> logger)
{
    /// <summary>Ponowienia tylko dla błędów przejściowych wysyłki (job rzuca wyjątek); błędy trwałe kończą job bez wyjątku.</summary>
    public const int MaxRetries = 3;

    private const string UniqueViolation = "23505";
    private const string DigestDateConstraint = "ux_digests_user_id_digest_date";

    [AutomaticRetry(Attempts = MaxRetries, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task SendAsync(Guid userId, DateOnly digestDate, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var settings = await dbContext.UserSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (settings is null || !settings.DigestEnabled)
        {
            // Konto usunięte albo digest wyłączony między schedulerem a wykonaniem.
            logger.LogInformation("Digest for user {UserId} on {DigestDate} skipped: digest disabled", userId, digestDate);
            return;
        }

        var digest = await ClaimAsync(dbContext, userId, digestDate, settings.TimeZoneId, cancellationToken);
        if (digest is null)
        {
            return;
        }

        try
        {
            await DeliverAsync(dbContext, digest, settings, cancellationToken);
        }
        catch (OperationCanceledException) when (digest.Status != DigestStatus.Sent)
        {
            // Zatrzymanie serwera albo timeout HTTP (TaskCanceledException) – zwalniamy wiersz jako Failed,
            // żeby ponowienie Hangfire mogło go przejąć. Kod zależy od etapu: bez zapisanej treści przerwało składanie.
            var errorCode = digest.HtmlBody is null ? DigestErrorCodes.ContentFailed : DigestErrorCodes.EmailDeliveryFailed;
            await FailAsync(dbContext, digest, errorCode);
            throw;
        }
    }

    private async Task DeliverAsync(DevPulseDbContext dbContext, Digest digest, UserSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.EmailConfirmed || string.IsNullOrWhiteSpace(settings.Email))
        {
            await FailAsync(dbContext, digest, DigestErrorCodes.NoRecipient);
            return;
        }

        RenderedDigestEmail email;
        try
        {
            email = digest.HtmlBody is null
                ? await ComposeAsync(dbContext, digest, settings.Email, cancellationToken)
                : RenderStored(digest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Także timeout HTTP przy odświeżaniu snapshotów (TaskCanceledException bez anulowania joba) – to błąd treści, nie wysyłki.
            logger.LogError(exception, "Digest {DigestId} content could not be built", digest.Id);
            await FailAsync(dbContext, digest, DigestErrorCodes.ContentFailed);
            return;
        }

        string? messageId;
        try
        {
            messageId = await emailSender.SendAsync(
                new EmailMessage(settings.Email, email.Subject, email.HtmlBody, email.PlainTextBody),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // Brak klucza albo adresu nadawcy – ponowienie nic nie zmieni.
            logger.LogError(exception, "Digest {DigestId} not sent: email sender is not configured", digest.Id);
            await FailAsync(dbContext, digest, DigestErrorCodes.EmailNotConfigured);
            return;
        }
        catch (EmailDeliveryException exception) when (!exception.IsTransient)
        {
            logger.LogError(exception, "Digest {DigestId} rejected by email provider", digest.Id);
            await FailAsync(dbContext, digest, DigestErrorCodes.EmailDeliveryFailed);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 429/5xx, błąd sieci – Failed teraz, a wyjątek uruchamia ponowienie Hangfire na tym samym wierszu.
            // Timeout HTTP (TaskCanceledException) obsługuje catch w SendAsync – z tym samym skutkiem.
            logger.LogWarning(exception, "Digest {DigestId} delivery failed; Hangfire will retry", digest.Id);
            await FailAsync(dbContext, digest, DigestErrorCodes.EmailDeliveryFailed);
            throw;
        }

        // Mail już wyszedł – status musi trafić do bazy także przy zatrzymaniu serwera, inaczej wiersz zostałby
        // Pending, a odzyskiwanie porzuconych prób wysłałoby ten sam mail drugi raz.
        digest.MarkSent(messageId, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Digest {DigestId} sent (AI used: {AiUsed})", digest.Id, digest.AiUsed);
    }

    /// <summary>
    /// Wstawia <c>Pending</c>; przy istniejącym wierszu przejmuje go atomowo tylko wtedy, gdy poprzednia próba się nie udała
    /// albo została porzucona (stary <c>Pending</c>). <c>Sent</c> i świeży <c>Pending</c> = koniec bez wysyłki.
    /// Przejęcie ustawia <c>created_at = now</c>: dla równoległej instancji wiersz jest od razu „świeży” (PostgreSQL
    /// sprawdza warunek ponownie po blokadzie wiersza), więc nie przejmie go drugi raz. <c>created_at</c> czyta tylko
    /// retencja – przesunięcie o najwyżej dobę jest bez znaczenia.
    /// </summary>
    private async Task<Digest?> ClaimAsync(
        DevPulseDbContext dbContext,
        Guid userId,
        DateOnly digestDate,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var digest = Digest.CreatePending(userId, digestDate, timeZoneId, now);
        dbContext.Digests.Add(digest);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return digest;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: DigestDateConstraint })
        {
            dbContext.Entry(digest).State = EntityState.Detached;
        }

        var stalePendingBefore = now - pipelineOptions.Value.StalePendingAfter;
        var claimed = await dbContext.Digests
            .Where(d => d.UserId == userId && d.DigestDate == digestDate)
            .Where(d => d.Status == DigestStatus.Failed || (d.Status == DigestStatus.Pending && d.CreatedAt < stalePendingBefore))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.Status, DigestStatus.Pending)
                    .SetProperty(d => d.CreatedAt, now)
                    .SetProperty(d => d.Error, (string?)null),
                cancellationToken);
        if (claimed == 0)
        {
            logger.LogInformation("Digest for user {UserId} on {DigestDate} already sent or in progress", userId, digestDate);
            return null;
        }

        return await dbContext.Digests.SingleAsync(d => d.UserId == userId && d.DigestDate == digestDate, cancellationToken);
    }

    /// <summary>Treść zapisywana przed wysyłką – ponowienie po błędzie SendGrid nie woła AI drugi raz.</summary>
    private async Task<RenderedDigestEmail> ComposeAsync(
        DevPulseDbContext dbContext,
        Digest digest,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        await RefreshSnapshotsIfStaleAsync(dbContext, digest.UserId, cancellationToken);
        var repositories = await LoadRepositoryStatesAsync(dbContext, digest.UserId, cancellationToken);

        var composition = composer.Compose(repositories, digest.DigestDate, timeProvider.GetUtcNow());
        var ai = repositories.Count == 0 ? null : await aiPrioritizer.PrioritizeAsync(repositories, cancellationToken);
        var priorities = ai?.Priorities ?? composition.FallbackPriorities;

        var email = renderer.Render(new DigestEmailModel(
            digest.DigestDate,
            digest.TimeZoneId,
            composition.Subject,
            priorities,
            composition.RepositorySummary,
            AiUsed: ai is not null,
            DashboardUrl()));

        digest.RecordContent(email.Subject, recipientEmail, priorities, composition.RepositorySummary, email.HtmlBody, ai?.Usage);
        await dbContext.SaveChangesAsync(cancellationToken);
        return email;
    }

    /// <summary>Ponowienie: ten sam temat, priorytety i stan repo co w zapisanym digeście (render jest deterministyczny).</summary>
    private RenderedDigestEmail RenderStored(Digest digest) => renderer.Render(new DigestEmailModel(
        digest.DigestDate,
        digest.TimeZoneId,
        digest.Subject ?? string.Empty,
        digest.Priorities ?? [],
        digest.RepositorySummary ?? [],
        digest.AiUsed,
        DashboardUrl()));

    /// <summary>Snapshot starszy niż interwał odświeżania (albo brak snapshotu) → jedno odświeżenie przed digestem (E3A).</summary>
    private async Task RefreshSnapshotsIfStaleAsync(DevPulseDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var freshSince = timeProvider.GetUtcNow() - snapshotOptions.Value.RefreshInterval;
        var hasStale = await dbContext.TrackedRepositories
            .Where(r => r.UserId == userId)
            .AnyAsync(r => !dbContext.RepositorySnapshots.Any(s => s.TrackedRepositoryId == r.Id && s.RefreshedAt >= freshSince), cancellationToken);

        if (hasStale)
        {
            // Błędy GitHuba job zapisuje w snapshotach i nie rzuca – digest powstaje wtedy z ostatnich znanych danych.
            await snapshotRefreshJob.RefreshUserAsync(userId, cancellationToken);
        }
    }

    /// <summary>Repo bez żadnego snapshotu pomijamy – digest nie może twierdzić, że ma 0 PR-ów, skoro danych jeszcze nie ma.</summary>
    private static async Task<IReadOnlyList<RepositoryDigestState>> LoadRepositoryStatesAsync(
        DevPulseDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from repository in dbContext.TrackedRepositories.AsNoTracking()
                where repository.UserId == userId
                join snapshot in dbContext.RepositorySnapshots.AsNoTracking() on repository.Id equals snapshot.TrackedRepositoryId
                select new { repository.FullName, Snapshot = snapshot })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new RepositoryDigestState(
                row.FullName,
                row.Snapshot.OpenPrCount,
                row.Snapshot.OpenIssueCount,
                row.Snapshot.CiStatus,
                row.Snapshot.LastActivityAt,
                row.Snapshot.OpenPullRequests.Count == 0 ? null : row.Snapshot.OpenPullRequests.Min(pr => pr.CreatedAt)))
            .ToList();
    }

    /// <summary>Błąd zapisywany zawsze, także przy zatrzymaniu serwera – inaczej wiersz zostałby Pending z niespójnym licznikiem prób.</summary>
    private async Task FailAsync(DevPulseDbContext dbContext, Digest digest, string errorCode)
    {
        digest.MarkFailed(errorCode, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        logger.LogWarning("Digest {DigestId} failed with {ErrorCode}", digest.Id, errorCode);
    }

    /// <summary>Link „Otwórz dashboard” – adres SPA z <c>App:PublicBaseUrl</c>; renderer i tak przepuszcza tylko http(s).</summary>
    private string? DashboardUrl() =>
        Uri.TryCreate(appOptions.Value.PublicBaseUrl, UriKind.Absolute, out var baseUrl) ? baseUrl.AbsoluteUri.TrimEnd('/') + "/" : null;
}
