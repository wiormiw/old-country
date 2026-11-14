using FluentValidation;
using Microsoft.Extensions.Options;

namespace OldCountry.Application.Pipeline.Validation;

/// <summary>
/// Request validation with FluentValidation. The wiring is explicit:
/// <list type="number">
/// <item>Each use case has an <c>AbstractValidator&lt;TRequest&gt;</c> next to its request type.</item>
/// <item>Its feature registers the validator with <see cref="AddValidator{T,TValidator}"/>.</item>
/// <item>Its endpoint in the Api runs the validator before the handler.</item>
/// </list>
/// Thus a handler receives only valid requests. The domain rules still protect the domain.
/// This layer gives clients clear messages before any work starts.
/// </summary>
public static class ValidationRegistration
{
    /// <summary>Validators have no state, so one instance serves the full app.</summary>
    public static IServiceCollection AddValidator<T, TValidator>(this IServiceCollection services)
        where TValidator : class, IValidator<T>
    {
        services.AddSingleton<IValidator<T>, TValidator>();
        return services;
    }

    /// <summary>Validates options with the same validators at startup. Use it with <c>ValidateOnStart</c>.</summary>
    public static OptionsBuilder<T> ValidateWithValidator<T, TValidator>(this OptionsBuilder<T> options)
        where T : class
        where TValidator : class, IValidator<T>
    {
        options.Services.AddValidator<T, TValidator>();
        options.Services.AddSingleton<IValidateOptions<T>, ValidatorOptionsAdapter<T>>();
        return options;
    }
}
