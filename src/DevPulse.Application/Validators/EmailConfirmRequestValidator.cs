using DevPulse.Application.DTOs.Settings;
using FluentValidation;

namespace DevPulse.Application.Validators;

public sealed class EmailConfirmRequestValidator : AbstractValidator<EmailConfirmRequest>
{
    /// <summary>32 losowe bajty w base64url bez paddingu.</summary>
    public const int TokenLength = 43;

    public EmailConfirmRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .Length(TokenLength)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Token musi być w formacie base64url.");
    }
}
