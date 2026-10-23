using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

/// <summary>M7–M9: IDigestComposer, IAiPrioritizer (Anthropic), IEmailSender (SendGrid), DigestSchedulerJob, SendDigestJob.</summary>
internal static class DigestModule
{
    public static IServiceCollection AddDigest(this IServiceCollection services, IConfiguration configuration) => services;
}
