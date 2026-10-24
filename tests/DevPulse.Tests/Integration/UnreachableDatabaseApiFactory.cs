using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevPulse.Tests.Integration;

/// <summary>API z connection stringiem do portu, na którym nic nie nasłuchuje – symuluje niedostępną bazę.</summary>
public sealed class UnreachableDatabaseApiFactory : WebApplicationFactory<Program>
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=devpulse;Username=devpulse;Password=devpulse;Timeout=2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", UnreachableConnectionString);
        builder.UseTestAuthSettings();

        // Serwer Hangfire bez bazy ponawia połączenie w nieskończoność i blokuje zamknięcie hosta na minuty.
        // Health check Hangfire nadal odpytuje storage, więc /health/ready i tak zgłasza Unhealthy.
        builder.ConfigureTestServices(services =>
        {
            var hangfireServer = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationFactory?.Method.DeclaringType?.Assembly == typeof(BackgroundJobServerHostedService).Assembly)
                .ToList();
            hangfireServer.Should().NotBeEmpty("rejestracja serwera Hangfire musi zostać znaleziona i usunięta");
            hangfireServer.ForEach(d => services.Remove(d));
        });
    }
}
