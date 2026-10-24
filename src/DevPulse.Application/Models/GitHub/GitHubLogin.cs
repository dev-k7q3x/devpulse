namespace DevPulse.Application.Models.GitHub;

/// <summary>
/// Wynik udanego callbacku GitHub OAuth – wejście upsertu użytkownika. <see cref="AccessToken"/> jest jawny,
/// więc <see cref="ToString"/> go pomija (rekord trafiony do logu nie może ujawnić tokenu).
/// </summary>
public sealed record GitHubLogin(
    long GitHubId,
    string Login,
    string? AvatarUrl,
    string? Email,
    string AccessToken,
    string TokenScopes)
{
    public override string ToString() => $"GitHubLogin {{ GitHubId = {GitHubId}, Login = {Login} }}";
}
