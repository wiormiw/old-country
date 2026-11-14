namespace OldCountry.Application.Tracks.GetTrack;

/// <summary>A playlist that holds a track.</summary>
/// <param name="Id">The id of the playlist.</param>
/// <param name="Name">The name of the playlist.</param>
public sealed record PlaylistRef(Guid Id, string Name);
