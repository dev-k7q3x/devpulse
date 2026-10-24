using DevPulse.Application.DTOs.Auth;
using DevPulse.Application.Interfaces;
using DevPulse.Application.Models.GitHub;
using DevPulse.Application.Options;
using DevPulse.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPulse.Infrastructure.Services;

internal sealed class AuthService(
    IDevPulseDbContext dbContext,
    ITokenProtector tokenProtector,
    TimeProvider timeProvider,
    IOptions<AdminOptions> adminOptions,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<Guid> SignInWithGitHubAsync(GitHubLogin login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);

        var now = timeProvider.GetUtcNow();
        var accessTokenEncrypted = tokenProtector.Protect(login.AccessToken);
        var tokenScopes = GitHubScopes.Normalize(login.TokenScopes);

        // Po id GitHuba – login może się zmienić.
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.GitHubId == login.GitHubId, cancellationToken);
        var isNewUser = user is null;
        if (user is null)
        {
            user = User.Create(login.GitHubId, login.Login, login.AvatarUrl, accessTokenEncrypted, tokenScopes, now);
            dbContext.Users.Add(user);
            dbContext.UserSettings.Add(UserSettings.CreateDefault(user.Id, login.Email, now));
        }
        else
        {
            // Ustawienia (w tym email zmieniony przez użytkownika) zostają bez zmian.
            user.RecordLogin(login.Login, login.AvatarUrl, accessTokenEncrypted, tokenScopes, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "GitHub sign-in for user {UserId} (GitHub id {GitHubId}), new account: {IsNewUser}",
            user.Id,
            user.GitHubId,
            isNewUser);

        return user.Id;
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Login, u.AvatarUrl, u.TokenInvalid, u.TokenScopes })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        var isAdmin = adminOptions.Value.Logins.Contains(user.Login, StringComparer.OrdinalIgnoreCase);

        return new CurrentUserResponse(
            user.Id,
            user.Login,
            user.AvatarUrl,
            GitHubTokenValid: !user.TokenInvalid,
            MissingScopes: GitHubScopes.Missing(user.TokenScopes),
            IsAdmin: isAdmin);
    }

    public async Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Jeden DELETE – kaskada FK w bazie usuwa wiersze zależne.
        var deleted = await dbContext.Users
            .Where(u => u.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Account {UserId} deleted", userId);
        }
    }
}
