using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Tracks.GetTrack;

public sealed record GetTrackQuery(TrackId Id) : IQuery<GetTrackResult>;
