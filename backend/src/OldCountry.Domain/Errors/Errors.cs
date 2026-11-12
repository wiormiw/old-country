namespace OldCountry.Domain.Errors;

/// <summary>An expected way for a use case to say no. Each has a stable code clients can switch on.</summary>
public interface IError
{
    string Code { get; }
}

public sealed record NotFoundError(string Resource, string Id) : IError
{
    public string Code => $"{Resource}.not_found";
}

public sealed record ConflictError(string Code, string Message) : IError;

/// <summary>Something outside our control (YouTube, a helper program) failed or refused.</summary>
public sealed record ExternalServiceError(string Code, string Message) : IError;

/// <summary>Field name → messages, the same shape as RFC 9457 validation problem details.</summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors) : IError
{
    public string Code => "validation";

    public static ValidationError For(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Either a valid value or the reason it isn't — what value object factories return.</summary>
public readonly union Validated<T>(T, ValidationError);

/// <summary>An invariant was broken by the caller: a bug, not user input. Becomes 409 at the edge.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
