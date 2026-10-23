using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M6: ISettingsService, zmiana i potwierdzanie adresu email.</summary>
internal static class SettingsModule
{
    public static IServiceCollection AddSettings(this IServiceCollection services, IConfiguration configuration) => services;
}
