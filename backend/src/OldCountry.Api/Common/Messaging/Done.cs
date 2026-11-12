namespace OldCountry.Api.Common.Messaging;

/// <summary>The success case of a union result that has nothing to return, e.g. <c>union X(Done, NotFoundError)</c>.</summary>
public sealed record Done
{
    public static readonly Done Value = new();

    private Done() { }
}
