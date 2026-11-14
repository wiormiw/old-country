namespace OldCountry.Application.Playlists;

/// <summary>The name and the description of a playlist, to create or change it.</summary>
public sealed record PlaylistDetailsRequest
{
    /// <summary>The name of the playlist, 1-200 characters.</summary>
    public required string Name { get; init; }

    /// <summary>An optional description in free text, up to 2000 characters.</summary>
    public string? Description { get; init; }
}
