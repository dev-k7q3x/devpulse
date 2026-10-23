using DevPulse.Application.Diagnostics;
using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

internal static class PersistenceModule
{
    public const string ConnectionStringName = "Default";
    public const string DatabaseHealthCheckName = "postgresql";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetRequiredConnectionString(configuration);

        // Fabryka (singleton) dla jobów i singletonów + DbContext scoped dla serwisów żądań.
        services.AddDbContextFactory<DevPulseDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IDevPulseDbContext>(provider => provider.GetRequiredService<DevPulseDbContext>());

        // Readiness: Database.CanConnectAsync(). Czas oczekiwania ogranicza parametr Timeout w connection stringu.
        services.AddHealthChecks()
            .AddDbContextCheck<DevPulseDbContext>(DatabaseHealthCheckName, tags: [HealthCheckTags.Ready]);

        return services;
    }

    /// <summary>
    /// Fail fast: bez connection stringa aplikacja nie startuje (zamiast wstawać z wiecznie Unhealthy /health/ready).
    /// Railway: ustaw ConnectionStrings__Default w formacie Npgsql (Host=…;Database=…), nie URI postgresql:// z DATABASE_URL.
    /// </summary>
    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Brak connection stringa 'ConnectionStrings:{ConnectionStringName}' (zmienna środowiskowa ConnectionStrings__{ConnectionStringName}).");
        }

        return connectionString;
    }
}
