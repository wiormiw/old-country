namespace OldCountry.Application.Abstractions.Messaging;

/// <summary>The success case of a union result with no value, for example <c>union X(Done, NotFoundError)</c>.</summary>
public sealed record Done
{
    public static readonly Done Value = new();

    private Done() { }
}
