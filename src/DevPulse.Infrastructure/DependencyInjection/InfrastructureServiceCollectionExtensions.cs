using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    // Każdy moduł ma własny plik – ścieżki równoległe edytują tylko swój moduł, nie tę listę.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddJobs(configuration);
        services.AddValidation();
        services.AddSecurity(configuration);
        services.AddGitHub(configuration);
        services.AddDashboard(configuration);
        services.AddSettings(configuration);
        services.AddDigest(configuration);
        services.AddDigestHistory(configuration);

        return services;
    }
}
