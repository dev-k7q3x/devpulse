namespace DevPulse.Application.Interfaces;

/// <summary>
/// Zalogowany użytkownik bieżącego żądania HTTP. Tylko dla kontrolerów – joby nie mają HttpContext
/// i dostają <c>userId</c> jawnie.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id użytkownika DevPulse. Rzuca <see cref="InvalidOperationException"/>, gdy żądanie nie jest uwierzytelnione.</summary>
    Guid UserId { get; }
}
