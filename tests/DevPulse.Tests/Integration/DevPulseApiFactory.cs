using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace DevPulse.Tests.Integration;

/// <summary>API na prawdziwym PostgreSQL w kontenerze (Testcontainers). Wymaga działającego Dockera.</summary>
public sealed class DevPulseApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Ten sam obraz co w docker-compose.yml.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    // Najpierw host (może jeszcze korzystać z bazy), dopiero potem kontener.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
    }
}

[CollectionDefinition(Name)]
public sealed class DevPulseApiCollection : ICollectionFixture<DevPulseApiFactory>
{
    public const string Name = "DevPulseApi";
}
