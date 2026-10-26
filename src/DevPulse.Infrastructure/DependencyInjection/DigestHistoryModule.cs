using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M10: IDigestService (historia 30 dni), RetentionJob.</summary>
internal static class DigestHistoryModule
{
    public static IServiceCollection AddDigestHistory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DigestHistoryOptions>().Bind(configuration.GetSection(DigestHistoryOptions.SectionName));

        services.AddScoped<IDigestService, DigestService>();
        services.AddTransient<RetentionJob>();
        services.AddHostedService<RetentionScheduler>();

        return services;
    }
}
