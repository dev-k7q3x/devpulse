using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M10: IDigestService (historia 30 dni), RetentionJob.</summary>
internal static class DigestHistoryModule
{
    public static IServiceCollection AddDigestHistory(this IServiceCollection services, IConfiguration configuration) => services;
}
