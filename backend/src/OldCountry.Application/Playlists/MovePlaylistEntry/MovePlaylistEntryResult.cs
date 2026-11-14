using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.MovePlaylistEntry;

public readonly union MovePlaylistEntryResult(Done, NotFoundError);
