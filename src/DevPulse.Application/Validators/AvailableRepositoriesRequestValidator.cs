using DevPulse.Application.DTOs.Repositories;
using FluentValidation;

namespace DevPulse.Application.Validators;

public sealed class AvailableRepositoriesRequestValidator : AbstractValidator<AvailableRepositoriesRequest>
{
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;

    public AvailableRepositoriesRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(SearchMaxLength);
    }
}
