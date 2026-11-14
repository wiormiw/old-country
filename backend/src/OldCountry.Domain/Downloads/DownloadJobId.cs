namespace OldCountry.Domain.Downloads;

/// <summary>
/// The id of a download job. It is a separate type, so the compiler stops the use of a different id in its place.
/// Version 7 GUIDs sort by creation time, so new index entries go to the end of the B-tree.
/// </summary>
public readonly record struct DownloadJobId(Guid Value)
{
    public static DownloadJobId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
