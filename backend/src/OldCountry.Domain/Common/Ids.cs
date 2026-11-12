namespace OldCountry.Domain.Common;

// Strongly-typed ids: a TrackId can never be passed where an ArtistId is expected.
// Version 7 GUIDs sort by creation time, which keeps index inserts at the end of the b-tree.

public readonly record struct TrackId(Guid Value)
{
    public static TrackId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ArtistId(Guid Value)
{
    public static ArtistId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PlaylistId(Guid Value)
{
    public static PlaylistId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DownloadJobId(Guid Value)
{
    public static DownloadJobId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
