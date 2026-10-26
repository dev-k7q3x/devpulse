using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M5: IDashboardService – czyta wyłącznie repository_snapshots.</summary>
internal static class DashboardModule
{
    public static IServiceCollection AddDashboard(this IServiceCollection services, IConfiguration configuration)
    {
        // SnapshotOptions (StaleAfter) binduje GitHubModule.
        services.AddScoped<IDashboardService, DashboardService>();

        return services;
    }
}
