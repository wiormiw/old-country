using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.Tracks.AddTrack;

public sealed record AddTrackCommand(AddTrackRequest Request) : ICommand<AddTrackResult>;
