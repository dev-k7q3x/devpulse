using DevPulse.Application.DTOs.Dashboard;

namespace DevPulse.Application.Interfaces;

/// <summary>Dashboard (M5). Czyta wyłącznie snapshoty z bazy – nigdy nie woła GitHuba.</summary>
public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Wersja danych dashboardu (wartość ETag w cudzysłowach) z lekkiego zapytania bez list PR – zmienia się, gdy zmieni się
    /// którakolwiek karta, także gdy snapshot przekroczy próg świeżości. Dzięki niej odpytanie co 15 min zwykle kończy się 304.
    /// </summary>
    Task<string> GetVersionAsync(Guid userId, CancellationToken cancellationToken);
}
