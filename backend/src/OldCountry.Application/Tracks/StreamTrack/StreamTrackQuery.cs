using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Tracks.StreamTrack;

public sealed record StreamTrackQuery(TrackId Id) : IQuery<StreamTrackResult>;
