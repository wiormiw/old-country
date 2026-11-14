using System.Text.Json;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Pipeline.Validation;

/// <summary>Changes FluentValidation failures into the one validation error shape of the API.</summary>
public static class ValidationFailures
{
    /// <summary>The keys are the JSON names that the client sent (<c>trackIds[0]</c>, not <c>TrackIds[0]</c>).</summary>
    public static ValidationError ToValidationError(this FluentValidation.Results.ValidationResult result) => new(
        result.Errors
            .GroupBy(failure => JsonKey(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));

    private static string JsonKey(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
