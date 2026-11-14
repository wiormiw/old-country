using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Playlists.RemovePlaylistEntry;

public readonly union RemovePlaylistEntryResult(Done, NotFoundError);
