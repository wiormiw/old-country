namespace OldCountry.Domain.Errors;

/// <summary>The requested item does not exist.</summary>
public sealed record NotFoundError(string Resource, string Id) : IError
{
    public string Code => $"{Resource}.not_found";
}
