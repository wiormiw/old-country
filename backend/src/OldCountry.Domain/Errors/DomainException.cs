namespace OldCountry.Domain.Errors;

/// <summary>
/// The caller broke an invariant. This is a bug, not bad user input.
/// The API returns it as 409 Conflict.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
