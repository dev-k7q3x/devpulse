using DevPulse.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Infrastructure.DependencyInjection;

internal static class ValidationModule
{
    /// <summary>Wszystkie walidatory z Application – nowy validator rejestruje się sam, wystarczy, że leży w tym assembly.</summary>
    public static IServiceCollection AddValidation(this IServiceCollection services) =>
        services.AddValidatorsFromAssemblyContaining<RepositoryAddRequestValidator>(ServiceLifetime.Singleton);
}
