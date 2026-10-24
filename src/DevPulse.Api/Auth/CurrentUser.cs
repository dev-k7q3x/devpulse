using System.Security.Claims;
using DevPulse.Application.Interfaces;

namespace DevPulse.Api.Auth;

/// <summary>Id użytkownika z claimu <see cref="DevPulseClaimTypes.UserId"/> w cookie sesji. Tylko dla żądań HTTP – nie dla jobów.</summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public bool IsAuthenticated => TryGetUserId(out _);

    public Guid UserId => TryGetUserId(out var userId)
        ? userId
        : throw new InvalidOperationException("Żądanie nie jest uwierzytelnione.");

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var principal = httpContextAccessor.HttpContext?.User;

        return principal?.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirstValue(DevPulseClaimTypes.UserId), out userId)
            && userId != Guid.Empty;
    }
}
