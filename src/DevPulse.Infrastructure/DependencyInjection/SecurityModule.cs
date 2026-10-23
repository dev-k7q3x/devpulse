using DevPulse.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M1 Auth: ITokenProtector (Data Protection, klucze w data_protection_keys), dostęp admina do /hangfire.</summary>
internal static class SecurityModule
{
    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminOptions>().Bind(configuration.GetSection(AdminOptions.SectionName));

        return services;
    }
}
