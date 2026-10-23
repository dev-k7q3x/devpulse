using DevPulse.Infrastructure.Persistence;
using DevPulse.Tests.Integration.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DevPulse.Tests.Integration;

/// <summary>
/// API na prawdziwym PostgreSQL w świeżym kontenerze (Testcontainers) z zaaplikowaną migracją InitialCreate.
/// Wymaga działającego Dockera. Testy nie zakładają danych z innych testów – każdy tworzy własne.
/// </summary>
public sealed class DevPulseApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Ten sam obraz co w docker-compose.yml.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    // Najpierw host (może jeszcze korzystać z bazy), dopiero potem kontener.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Kontekst poza DI aplikacji – do seedowania i asercji na bazie.</summary>
    public DevPulseDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPulseDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // Endpointy testowe do sprawdzania wspólnego potoku (walidacja, mapowanie błędów, rate limiting).
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(PipelineTestController).Assembly));
    }
}

[CollectionDefinition(Name)]
public sealed class DevPulseApiCollection : ICollectionFixture<DevPulseApiFactory>
{
    public const string Name = "DevPulseApi";
}
