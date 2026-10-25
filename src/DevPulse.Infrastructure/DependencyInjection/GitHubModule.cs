using System.Net;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Infrastructure.GitHub;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M2–M4: IGitHubClient (GraphQL + REST, GitHubRateLimitHandler), IRepositoryService, SnapshotRefreshJob.</summary>
internal static class GitHubModule
{
    public static IServiceCollection AddGitHub(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RepositoriesOptions>().Bind(configuration.GetSection(RepositoriesOptions.SectionName));
        services.AddOptions<SnapshotOptions>().Bind(configuration.GetSection(SnapshotOptions.SectionName));
        services.AddOptions<GitHubOptions>().Bind(configuration.GetSection(GitHubOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<GitHubRateLimitBudget>();
        services.AddSingleton<IGitHubTokenStore, GitHubTokenStore>();

        // Bez IHttpClientFactory (brak Microsoft.Extensions.Http w Infrastructure): jeden HttpClient na proces,
        // PooledConnectionLifetime odświeża połączenia (zmiany DNS), handler limitu w łańcuchu.
        services.AddSingleton<IGitHubClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<GitHubOptions>>();
            var rateLimitHandler = new GitHubRateLimitHandler(
                provider.GetRequiredService<GitHubRateLimitBudget>(),
                provider.GetRequiredService<IGitHubTokenStore>(),
                options,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<GitHubRateLimitHandler>>())
            {
                InnerHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    AutomaticDecompression = DecompressionMethods.All,
                },
            };

            return new GitHubClient(
                CreateHttpClient(rateLimitHandler, options.Value),
                provider.GetRequiredService<IGitHubTokenStore>(),
                provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                options,
                provider.GetRequiredService<ILogger<GitHubClient>>());
        });

        services.AddScoped<IRepositoryService, RepositoryService>();
        services.AddTransient<SnapshotRefreshJob>();
        services.AddHostedService<SnapshotRefreshScheduler>();

        return services;
    }

    internal static HttpClient CreateHttpClient(HttpMessageHandler handler, GitHubOptions options)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.ApiBaseUrl,
            Timeout = options.RequestTimeout,
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DevPulse");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return httpClient;
    }
}
