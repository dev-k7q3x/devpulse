namespace DevPulse.Api.Auth;

/// <summary>Sekcja <c>Auth</c>. Sekret klienta OAuth tylko w env / user-secrets (<c>Auth__GitHub__ClientSecret</c>).</summary>
internal sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Ważność cookie sesji (przesuwana przy aktywności).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

    public GitHubOAuthOptions GitHub { get; set; } = new();
}

/// <summary>GitHub OAuth App: callback <c>https://&lt;domena&gt;/signin-github</c>.</summary>
internal sealed class GitHubOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
