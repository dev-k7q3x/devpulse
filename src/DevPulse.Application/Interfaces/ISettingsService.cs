using DevPulse.Application.DTOs.Settings;

namespace DevPulse.Application.Interfaces;

/// <summary>Ustawienia digestu i zmiana adresu email (M6).</summary>
public interface ISettingsService
{
    Task<SettingsResponse> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<SettingsResponse> UpdateAsync(Guid userId, SettingsUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Zapisuje oczekujący adres i enqueue'uje mail z linkiem. 409, gdy to już aktualny potwierdzony adres.</summary>
    Task<SettingsResponse> StartEmailChangeAsync(Guid userId, EmailChangeRequest request, CancellationToken cancellationToken);

    /// <summary>Idempotentne.</summary>
    Task CancelEmailChangeAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Bez zalogowania – wiersz wyszukiwany po SHA-256 tokenu. <see cref="Errors.InvalidTokenException"/> dla każdego błędu tokenu.</summary>
    Task<EmailConfirmResponse> ConfirmEmailAsync(EmailConfirmRequest request, CancellationToken cancellationToken);
}
