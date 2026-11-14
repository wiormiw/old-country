using FluentValidation;
using Microsoft.Extensions.Options;

namespace OldCountry.Application.Pipeline.Validation;

/// <summary>Runs a FluentValidation validator as an options validator, so a bad setting stops the startup.</summary>
internal sealed class ValidatorOptionsAdapter<T>(IValidator<T> validator) : IValidateOptions<T> where T : class
{
    public ValidateOptionsResult Validate(string? name, T options)
    {
        var result = validator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors.Select(failure => $"{typeof(T).Name}.{failure.PropertyName}: {failure.ErrorMessage}"));
    }
}
