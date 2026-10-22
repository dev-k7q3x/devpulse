using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    // Każdy moduł (Persistence, GitHub, Digest, Settings, …) ma własny plik – ścieżki równoległe nie edytują tych samych miejsc.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);

        return services;
    }
}
