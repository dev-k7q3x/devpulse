using System.Text.Json.Serialization;

namespace DevPulse.Application.DTOs.Auth;

/// <summary>Query <c>GET /api/auth/login</c>. Akceptowana tylko ścieżka lokalna (ochrona przed open redirect).</summary>
public sealed record AuthLoginRequest
{
    public string? ReturnUrl { get; init; }
}

/// <summary><c>GET /api/auth/me</c> – SPA wywołuje przy starcie, na tej podstawie działa ProtectedRoute.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Login,
    string? AvatarUrl,
    [property: JsonPropertyName("githubTokenValid")] bool GitHubTokenValid,
    IReadOnlyList<string> MissingScopes,
    bool IsAdmin);
