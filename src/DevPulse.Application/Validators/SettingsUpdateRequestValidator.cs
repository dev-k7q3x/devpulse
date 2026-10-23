using DevPulse.Application.DTOs.Settings;
using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using FluentValidation;

namespace DevPulse.Application.Validators;

public sealed class SettingsUpdateRequestValidator : AbstractValidator<SettingsUpdateRequest>
{
    public SettingsUpdateRequestValidator()
    {
        RuleFor(x => x.SendHour).InclusiveBetween(SendHour.Min, SendHour.Max);

        RuleFor(x => x.TimeZoneId)
            .NotEmpty()
            .MaximumLength(UserSettings.TimeZoneIdMaxLength)
            .Must(BeIanaTimeZone).WithMessage("Nieznana strefa czasowa IANA (np. Europe/Warsaw).");
    }

    /// <summary>Strefa musi istnieć w systemie i być identyfikatorem IANA – nie przyjmujemy id Windows.</summary>
    private static bool BeIanaTimeZone(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _);
}
