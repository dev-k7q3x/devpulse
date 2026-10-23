using DevPulse.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M2–M4: IGitHubClient (GraphQL + REST, GitHubRateLimitHandler), IRepositoryService, SnapshotRefreshJob.</summary>
internal static class GitHubModule
{
    public static IServiceCollection AddGitHub(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RepositoriesOptions>().Bind(configuration.GetSection(RepositoriesOptions.SectionName));
        services.AddOptions<SnapshotOptions>().Bind(configuration.GetSection(SnapshotOptions.SectionName));

        return services;
    }
}
