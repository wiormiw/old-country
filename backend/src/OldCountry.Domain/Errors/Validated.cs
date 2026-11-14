namespace OldCountry.Domain.Errors;

/// <summary>A valid value, or the reason that the value is not valid. Value object factories return it.</summary>
public readonly union Validated<T>(T, ValidationError);
