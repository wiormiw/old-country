using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Errors;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Common.Validation;

/// <summary>
/// Request validation with FluentValidation, wired explicitly:
/// <list type="number">
/// <item>each slice declares an <c>AbstractValidator&lt;TRequest&gt;</c> next to its request type,</item>
/// <item>registers it with <see cref="ValidationRegistration.AddValidator{T,TValidator}"/>,</item>
/// <item>and opts its endpoint in with <see cref="ValidationRegistration.Validate{TRequest}"/>.</item>
/// </list>
/// The handler then only ever sees requests that passed. Domain rules still guard the domain;
/// this layer exists to give clients precise, friendly messages before any work starts.
/// </summary>
internal static class ValidationRegistration
{
    /// <summary>Validators are stateless, so one instance serves the whole app.</summary>
    public static IServiceCollection AddValidator<T, TValidator>(this IServiceCollection services)
        where TValidator : class, IValidator<T>
    {
        services.AddSingleton<IValidator<T>, TValidator>();
        return services;
    }

    /// <summary>Runs <see cref="IValidator{T}"/> for the endpoint's <typeparamref name="TRequest"/> argument first.</summary>
    public static RouteHandlerBuilder Validate<TRequest>(this RouteHandlerBuilder endpoint) => endpoint
        .AddEndpointFilter<ValidationFilter<TRequest>>()
        .ProducesValidationProblem();

    /// <summary>Validates options with the same validators, at startup (with <c>ValidateOnStart</c>).</summary>
    public static OptionsBuilder<T> ValidateWithValidator<T, TValidator>(this OptionsBuilder<T> options)
        where T : class
        where TValidator : class, IValidator<T>
    {
        options.Services.AddValidator<T, TValidator>();
        options.Services.AddSingleton<IValidateOptions<T>, ValidatorOptionsAdapter<T>>();
        return options;
    }
}

/// <summary>Turns FluentValidation failures into the API's one validation problem shape.</summary>
internal static class ValidationFailures
{
    /// <summary>Keys use the JSON names clients sent (<c>trackIds[0]</c>, not <c>TrackIds[0]</c>).</summary>
    public static ValidationError ToValidationError(this FluentValidation.Results.ValidationResult result) => new(
        result.Errors
            .GroupBy(failure => JsonKey(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));

    private static string JsonKey(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

internal sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Binding has already happened; a missing body was rejected by the framework before here.
        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
            return await next(context);

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        return result.IsValid ? await next(context) : result.ToValidationError().ToProblem();
    }
}

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
