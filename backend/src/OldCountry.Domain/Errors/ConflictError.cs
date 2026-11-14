namespace OldCountry.Domain.Errors;

/// <summary>The request conflicts with the current state, for example a duplicate.</summary>
public sealed record ConflictError(string Code, string Message) : IError;
