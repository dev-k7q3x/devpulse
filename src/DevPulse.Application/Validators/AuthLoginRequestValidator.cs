using DevPulse.Application.DTOs.Auth;
using FluentValidation;

namespace DevPulse.Application.Validators;

public sealed class AuthLoginRequestValidator : AbstractValidator<AuthLoginRequest>
{
    public const int ReturnUrlMaxLength = 2048;

    public AuthLoginRequestValidator()
    {
        RuleFor(x => x.ReturnUrl)
            .MaximumLength(ReturnUrlMaxLength)
            .Must(BeLocalUrl).WithMessage("returnUrl musi być lokalną ścieżką aplikacji.")
            .When(x => x.ReturnUrl is not null);
    }

    /// <summary>Ta sama reguła co <c>IUrlHelper.IsLocalUrl</c>: <c>/ścieżka</c>, ale nie <c>//host</c> ani <c>/\host</c>.</summary>
    private static bool BeLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.Any(char.IsControl))
        {
            return false;
        }

        return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
    }
}
