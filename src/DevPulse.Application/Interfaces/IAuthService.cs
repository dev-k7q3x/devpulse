using DevPulse.Application.DTOs.Auth;
using DevPulse.Application.Models.GitHub;

namespace DevPulse.Application.Interfaces;

/// <summary>M1: konto użytkownika – upsert przy logowaniu GitHubem, profil dla <c>/api/auth/me</c>, usunięcie konta.</summary>
public interface IAuthService
{
    /// <summary>
    /// Tworzy użytkownika z domyślnymi ustawieniami albo odświeża profil istniejącego (po id GitHuba, nie loginie).
    /// Token jest szyfrowany przed zapisem. Zwraca id użytkownika DevPulse do claimu sesji.
    /// </summary>
    Task<Guid> SignInWithGitHubAsync(GitHubLogin login, CancellationToken cancellationToken);

    /// <returns><c>null</c>, gdy użytkownik już nie istnieje (np. konto usunięte w innej sesji).</returns>
    Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Trwale usuwa konto – kaskada w bazie kasuje ustawienia, repo, snapshoty i digesty. Idempotentne.</summary>
    Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken);
}
