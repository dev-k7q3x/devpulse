using DevPulse.Application.Diagnostics;
using DevPulse.Infrastructure.Jobs;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>
/// Hangfire w procesie API, storage w schemacie <c>hangfire</c> (poza migracjami EF). Recurring joby rejestruje moduł,
/// do którego job należy. Joby nie mają HttpContext – <c>userId</c> przekazuj jawnie.
/// </summary>
internal static class JobsModule
{
    public const string SchemaName = "hangfire";
    public const string HealthCheckName = "hangfire";

    public static IServiceCollection AddJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = PersistenceModule.GetRequiredConnectionString(configuration);

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = SchemaName, PrepareSchemaIfNecessary = true }));

        services.AddHangfireServer();

        services.AddHealthChecks()
            .AddCheck<HangfireHealthCheck>(HealthCheckName, tags: [HealthCheckTags.Ready]);

        return services;
    }
}
