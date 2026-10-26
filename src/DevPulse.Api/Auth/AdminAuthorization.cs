using System.Security.Claims;
using DevPulse.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace DevPulse.Api.Auth;

/// <summary>Nazwy polityk autoryzacji poza FallbackPolicy.</summary>
public static class AuthorizationPolicies
{
    /// <summary>GitHub loginy z <c>Admin:Logins</c> – panel <c>/hangfire</c>.</summary>
    public const string Admin = "Admin";
}

/// <summary>Wymaganie polityki <see cref="AuthorizationPolicies.Admin"/>.</summary>
internal sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Cookie niesie tylko id DevPulse, a login GitHub może się zmienić – uprawnienie liczone przy każdym żądaniu
/// z aktualnego loginu w bazie (ta sama reguła co <c>isAdmin</c> w <c>/api/auth/me</c>).
/// </summary>
internal sealed class AdminAuthorizationHandler(IAuthService authService) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(DevPulseClaimTypes.UserId), out var userId))
        {
            return;
        }

        var cancellationToken = context.Resource is HttpContext httpContext ? httpContext.RequestAborted : CancellationToken.None;
        var user = await authService.GetCurrentUserAsync(userId, cancellationToken);
        if (user is { IsAdmin: true })
        {
            context.Succeed(requirement);
        }
    }
}
