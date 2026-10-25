using System.Security.Cryptography;
using DevPulse.Application.Errors;
using DevPulse.Application.Interfaces;
using DevPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevPulse.Infrastructure.GitHub;

/// <summary>Token GitHub użytkownika dla klienta API (żądania i joby) oraz oznaczanie go jako nieważny po 401.</summary>
internal interface IGitHubTokenStore
{
    /// <summary>Jawny token. <see cref="GitHubTokenInvalidException"/>, gdy użytkownika nie ma, token jest oznaczony jako nieważny albo nie da się go odszyfrować.</summary>
    Task<string> GetAccessTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task MarkTokenInvalidAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Singleton – kontekst bazy z <see cref="IDbContextFactory{TContext}"/>, bo klient GitHub żyje dłużej niż żądanie.</summary>
internal sealed class GitHubTokenStore(
    IDbContextFactory<DevPulseDbContext> dbContextFactory,
    ITokenProtector tokenProtector,
    TimeProvider timeProvider,
    ILogger<GitHubTokenStore> logger) : IGitHubTokenStore
{
    public async Task<string> GetAccessTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.AccessTokenEncrypted, u.TokenInvalid })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null || user.TokenInvalid)
        {
            throw new GitHubTokenInvalidException();
        }

        try
        {
            return tokenProtector.Unprotect(user.AccessTokenEncrypted);
        }
        catch (CryptographicException exception)
        {
            // Np. utracony pierścień kluczy albo certyfikat – jedynym wyjściem jest ponowne logowanie. Oznaczenie tokenu
            // wyłącza użytkownika z odświeżania, zamiast powtarzać ten sam błąd co 15 min.
            logger.LogWarning(exception, "GitHub token of user {UserId} cannot be decrypted", userId);
            await MarkTokenInvalidAsync(userId, cancellationToken);
            throw new GitHubTokenInvalidException();
        }
    }

    public async Task MarkTokenInvalidAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.TokenInvalid)
        {
            return;
        }

        user.MarkTokenInvalid(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning("GitHub rejected the token of user {UserId}; marked as invalid", userId);
    }
}
