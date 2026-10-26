using DevPulse.Application.Digests;
using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Ai;
using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M7–M9: IDigestComposer, IAiPrioritizer (Anthropic), IEmailSender (SendGrid), DigestSchedulerJob, SendDigestJob.</summary>
internal static class DigestModule
{
    /// <summary>Połączenia odnawiane co kwadrans – singleton HttpClient bez przeterminowanego DNS (zamiast IHttpClientFactory).</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(15);

    public static IServiceCollection AddDigest(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DigestRulesOptions>().Bind(configuration.GetSection(DigestRulesOptions.SectionName));
        services.AddSingleton<IDigestComposer, DigestComposer>();
        services.AddSingleton<IDigestEmailRenderer, DigestEmailRenderer>();

        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .Configure(options => options.ApiKey = configuration[AnthropicOptions.ApiKeyVariable]);
        services.AddSingleton<IAiPrioritizer>(provider => new AnthropicPrioritizer(
            CreateHttpClient(),
            provider.GetRequiredService<IOptions<AnthropicOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<AnthropicPrioritizer>>()));

        services.AddOptions<SendGridOptions>().Bind(configuration.GetSection(SendGridOptions.SectionName));
        services.AddSingleton<IEmailSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<SendGridOptions>>();
            var httpClient = CreateHttpClient();
            httpClient.Timeout = options.Value.Timeout;
            return new SendGridEmailSender(httpClient, options, provider.GetRequiredService<ILogger<SendGridEmailSender>>());
        });

        // M9: godzinowy scheduler (recurring rejestrowany w tle) i wysyłka digestu jednego użytkownika.
        services.AddOptions<DigestPipelineOptions>().Bind(configuration.GetSection(DigestPipelineOptions.SectionName));
        services.AddTransient<DigestSchedulerJob>();
        services.AddTransient<SendDigestJob>();
        services.AddHostedService<DigestScheduler>();

        return services;
    }

    // Timeout wywołań AI liczy AnthropicPrioritizer (TimeProvider), więc tu tylko bezpiecznik.
    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime }) { Timeout = TimeSpan.FromMinutes(2) };
}
