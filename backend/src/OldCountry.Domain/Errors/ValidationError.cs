namespace OldCountry.Domain.Errors;

/// <summary>Field names and their messages. The shape is the same as RFC 9457 validation problem details.</summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors) : IError
{
    public string Code => "validation";

    public static ValidationError For(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}
