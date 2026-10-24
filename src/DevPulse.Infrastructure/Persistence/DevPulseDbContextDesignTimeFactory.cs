using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevPulse.Infrastructure.Persistence;

/// <summary>
/// Kontekst dla <c>dotnet ef</c> (migracje, <c>database update</c>, migration bundle) bez budowania hosta API –
/// host wymaga sekretów (GitHub OAuth, certyfikat Data Protection), których nie ma przy buildzie obrazu ani w pre-deploy.
/// Connection string: <c>--connection</c> narzędzia, zmienna <c>ConnectionStrings__Default</c> albo lokalny PostgreSQL z docker-compose.
/// </summary>
internal sealed class DevPulseDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DevPulseDbContext>
{
    private const string LocalConnectionString =
        "Host=localhost;Port=5432;Database=devpulse;Username=devpulse;Password=devpulse;Timeout=5";

    public DevPulseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // W obrazie (ASPNETCORE_ENVIRONMENT=Production) brak zmiennej to błąd konfiguracji pre-deploy –
            // cichy fallback na localhost dałby mylący błąd połączenia.
            if (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Brak zmiennej środowiskowej ConnectionStrings__Default (format Npgsql: Host=…;Database=…;Username=…;Password=…).");
            }

            connectionString = LocalConnectionString;
        }

        var options = new DbContextOptionsBuilder<DevPulseDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DevPulseDbContext(options);
    }
}
