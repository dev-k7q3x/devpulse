using DevPulse.Application.DTOs.Settings;
using DevPulse.Domain.Entities;
using FluentValidation;

namespace DevPulse.Application.Validators;

public sealed class EmailChangeRequestValidator : AbstractValidator<EmailChangeRequest>
{
    public EmailChangeRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(UserSettings.EmailMaxLength)
            .EmailAddress();
    }
}
