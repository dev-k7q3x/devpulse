using DevPulse.Application.DTOs.Repositories;

namespace DevPulse.Application.Interfaces;

/// <summary>Śledzone repozytoria (M3). Każda metoda filtruje po <paramref name="userId"/> – cudze repo to 404.</summary>
public interface IRepositoryService
{
    /// <summary>Woła GitHuba (cache 10 min per użytkownik). Błędy GitHuba zgodnie z <see cref="IGitHubClient"/>.</summary>
    Task<AvailableRepositoriesResponse> GetAvailableAsync(
        Guid userId,
        AvailableRepositoriesRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackedRepositoryResponse>> GetTrackedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Dodaje repo i enqueue'uje pierwszy snapshot. 404 (brak dostępu), 409 (już śledzone), 422 (limit).</summary>
    Task<TrackedRepositoryResponse> AddAsync(Guid userId, RepositoryAddRequest request, CancellationToken cancellationToken);

    /// <summary>404, gdy repo nie istnieje albo należy do innego użytkownika.</summary>
    Task RemoveAsync(Guid userId, Guid trackedRepositoryId, CancellationToken cancellationToken);
}
