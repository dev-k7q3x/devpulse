using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M6: ISettingsService, zmiana i potwierdzanie adresu email.</summary>
internal static class SettingsModule
{
    public static IServiceCollection AddSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.SectionName));
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<SendEmailConfirmationJob>();

        // Atrapa do czasu scalenia E3B – SendGridEmailSender (zwykłe Add w DigestModule) ma pierwszeństwo.
        services.TryAddSingleton<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
