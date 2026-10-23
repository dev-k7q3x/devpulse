using DevPulse.Application.DTOs.Dashboard;

namespace DevPulse.Application.Interfaces;

/// <summary>Dashboard (M5). Czyta wyłącznie snapshoty z bazy – nigdy nie woła GitHuba.</summary>
public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(Guid userId, CancellationToken cancellationToken);
}
