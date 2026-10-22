using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>API z connection stringiem do portu, na którym nic nie nasłuchuje – symuluje niedostępną bazę.</summary>
public sealed class UnreachableDatabaseApiFactory : WebApplicationFactory<Program>
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=devpulse;Username=devpulse;Password=devpulse;Timeout=2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", UnreachableConnectionString);
    }
}
