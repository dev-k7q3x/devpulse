using DevPulse.Application.DigestHistory;
using DevPulse.Application.DTOs.Digests;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Services;

/// <summary>
/// Historia digestów (plan 03 §5). Każde zapytanie filtruje jawnie po <c>userId</c> i po oknie historii – digest starszy
/// niż okno nie jest widoczny także między przebiegami <c>RetentionJob</c>. Cudzy albo spoza okna → 404.
/// </summary>
internal sealed class DigestService(
    IDevPulseDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<DigestHistoryOptions> options) : IDigestService
{
    public async Task<IReadOnlyList<DigestListItem>> GetListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var firstVisibleDate = await GetFirstVisibleDateAsync(userId, cancellationToken);

        // Projekcja bez html_body i repository_summary – lista ma kilka KB.
        var rows = await dbContext.Digests
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.DigestDate >= firstVisibleDate)
            .OrderByDescending(d => d.DigestDate)
            .Select(d => new { d.Id, d.DigestDate, d.Status, d.Subject, d.SentAt, d.AiUsed, d.Priorities })
            .ToListAsync(cancellationToken);

        return rows
            .Select(d => new DigestListItem(d.Id, d.DigestDate, d.Status, d.Subject, d.SentAt, d.AiUsed, MapPriorities(d.Priorities)))
            .ToList();
    }

    public async Task<DigestDetailResponse> GetAsync(Guid userId, Guid digestId, CancellationToken cancellationToken)
    {
        var firstVisibleDate = await GetFirstVisibleDateAsync(userId, cancellationToken);

        var digest = await dbContext.Digests
            .AsNoTracking()
            .Where(d => d.Id == digestId && d.UserId == userId && d.DigestDate >= firstVisibleDate)
            .Select(d => new
            {
                d.Id,
                d.DigestDate,
                d.Status,
                d.Subject,
                d.SentAt,
                d.AiUsed,
                d.Priorities,
                d.TimeZoneId,
                d.RecipientEmail,
                d.AttemptCount,
                d.Error,
                HasHtml = d.HtmlBody != null,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Nie znaleziono digestu.");

        return new DigestDetailResponse(
            digest.Id,
            digest.DigestDate,
            digest.Status,
            digest.Subject,
            digest.SentAt,
            digest.AiUsed,
            MapPriorities(digest.Priorities),
            digest.TimeZoneId,
            digest.RecipientEmail,
            digest.AttemptCount,
            DigestHistoryRules.UserFacingError(digest.Status, digest.Error),
            digest.HasHtml);
    }

    public async Task<string> GetHtmlAsync(Guid userId, Guid digestId, CancellationToken cancellationToken)
    {
        var firstVisibleDate = await GetFirstVisibleDateAsync(userId, cancellationToken);

        var html = await dbContext.Digests
            .AsNoTracking()
            .Where(d => d.Id == digestId && d.UserId == userId && d.DigestDate >= firstVisibleDate)
            .Select(d => d.HtmlBody)
            .SingleOrDefaultAsync(cancellationToken);

        // Brak digestu i digest bez treści (Failed przed renderem) to ten sam 404.
        return html ?? throw new NotFoundException("Nie znaleziono podglądu digestu.");
    }

    private async Task<DateOnly> GetFirstVisibleDateAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Okno liczone w bieżącej strefie użytkownika (contract: digest_date >= today_local - 30).
        var timeZoneId = await dbContext.UserSettings
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);

        return DigestHistoryRules.FirstVisibleDate(timeProvider.GetUtcNow(), timeZoneId, options.Value.RetentionPeriod);
    }

    private static IReadOnlyList<DigestPriorityItem> MapPriorities(List<DigestPriority>? priorities) =>
        priorities?.Select(p => new DigestPriorityItem(p.RepoFullName, p.Text, p.Severity)).ToList() ?? [];
}
