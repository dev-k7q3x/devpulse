using DevPulse.Application.DTOs.Digests;

namespace DevPulse.Application.Interfaces;

/// <summary>Historia digestów (M10) – ostatnie 30 dni. Cudzy digest to 404.</summary>
public interface IDigestService
{
    /// <summary>Projekcja bez <c>html_body</c>.</summary>
    Task<IReadOnlyList<DigestListItem>> GetListAsync(Guid userId, CancellationToken cancellationToken);

    Task<DigestDetailResponse> GetAsync(Guid userId, Guid digestId, CancellationToken cancellationToken);

    /// <summary>Wyrenderowany HTML maila. 404 także wtedy, gdy digest nie ma treści (np. Failed przed renderem).</summary>
    Task<string> GetHtmlAsync(Guid userId, Guid digestId, CancellationToken cancellationToken);
}
