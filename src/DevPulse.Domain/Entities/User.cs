namespace DevPulse.Domain.Entities;

/// <summary>Konto tworzone przy pierwszym logowaniu GitHub OAuth. Identyfikowane po <see cref="GitHubId"/> – login może się zmienić.</summary>
public sealed class User
{
    public const int LoginMaxLength = 39;
    public const int AvatarUrlMaxLength = 512;
    public const int TokenScopesMaxLength = 256;

    private User()
    {
    }

    public Guid Id { get; private set; }

    public long GitHubId { get; private set; }

    public string Login { get; private set; } = string.Empty;

    public string? AvatarUrl { get; private set; }

    /// <summary>Payload Data Protection. Nigdy nie trafia do logów ani DTO.</summary>
    public string AccessTokenEncrypted { get; private set; } = string.Empty;

    public string TokenScopes { get; private set; } = string.Empty;

    public bool TokenInvalid { get; private set; }

    public DateTimeOffset TokenUpdatedAt { get; private set; }

    public DateTimeOffset LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Create(
        long gitHubId,
        string login,
        string? avatarUrl,
        string accessTokenEncrypted,
        string tokenScopes,
        DateTimeOffset now)
    {
        if (gitHubId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gitHubId), gitHubId, "Id GitHuba musi być dodatnie.");
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            GitHubId = gitHubId,
            CreatedAt = Guard.Utc(now, nameof(now)),
        };
        user.RecordLogin(login, avatarUrl, accessTokenEncrypted, tokenScopes, now);
        return user;
    }

    /// <summary>Każde logowanie odświeża profil i nadpisuje token (szyfrowany bieżącym kluczem), zdejmując flagę nieważności.</summary>
    public void RecordLogin(string login, string? avatarUrl, string accessTokenEncrypted, string tokenScopes, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessTokenEncrypted);
        ArgumentNullException.ThrowIfNull(tokenScopes);
        Guard.Utc(now, nameof(now));

        if (login.Length > LoginMaxLength)
        {
            throw new ArgumentException($"Login GitHuba ma maksymalnie {LoginMaxLength} znaków.", nameof(login));
        }

        // Bez obcinania – ucięta lista dałaby fałszywe missingScopes. OAuth App przyznaje najwyżej żądane scopes,
        // więc przekroczenie limitu oznacza błąd w kodzie, nie dane od użytkownika.
        if (tokenScopes.Length > TokenScopesMaxLength)
        {
            throw new ArgumentException($"Lista scopes ma maksymalnie {TokenScopesMaxLength} znaków.", nameof(tokenScopes));
        }

        Login = login;
        AvatarUrl = Guard.Truncate(avatarUrl, AvatarUrlMaxLength);
        AccessTokenEncrypted = accessTokenEncrypted;
        TokenScopes = tokenScopes;
        TokenInvalid = false;
        TokenUpdatedAt = now;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    /// <summary>Po 401 z GitHuba: joby pomijają użytkownika, UI wymusza ponowne logowanie.</summary>
    public void MarkTokenInvalid(DateTimeOffset now)
    {
        TokenInvalid = true;
        UpdatedAt = Guard.Utc(now, nameof(now));
    }
}
