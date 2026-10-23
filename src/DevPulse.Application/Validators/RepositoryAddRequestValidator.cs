using DevPulse.Application.DTOs.Repositories;
using FluentValidation;

namespace DevPulse.Application.Validators;

public sealed class RepositoryAddRequestValidator : AbstractValidator<RepositoryAddRequest>
{
    public RepositoryAddRequestValidator()
    {
        // Nazwa jak w JSON kontraktu (githubRepoId), żeby klucz w errors zgadzał się z polem formularza.
        RuleFor(x => x.GitHubRepoId).GreaterThan(0).OverridePropertyName("githubRepoId");
    }
}
