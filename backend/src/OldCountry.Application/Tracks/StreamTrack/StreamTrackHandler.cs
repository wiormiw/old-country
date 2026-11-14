using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Library;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Tracks.StreamTrack;

internal sealed partial class StreamTrackHandler(IAppDbContext db, IOptions<LibraryOptions> library, ILogger<StreamTrackHandler> logger)
    : IQueryHandler<StreamTrackQuery, StreamTrackResult>
{
    public async Task<StreamTrackResult> Handle(StreamTrackQuery query, CancellationToken cancellationToken)
    {
        var path = await db.Tracks.AsNoTracking()
            .Where(track => track.Id == query.Id && track.LocalFile != null)
            .Select(track => track.LocalFile!.Path)
            .SingleOrDefaultAsync(cancellationToken);
        if (path is null) return new NotFoundError("track_audio", query.Id.ToString());

        // The paths come from clients (POST /api/tracks) and from scans. Serve only files in the library folders.
        // Otherwise this endpoint could read any file on the server.
        if (!LibraryPaths.IsInsideLibrary(path, library.Value.Roots))
        {
            Log.OutsideLibrary(logger, query.Id.Value);
            return new NotFoundError("track_audio", query.Id.ToString());
        }

        if (!File.Exists(path))
        {
            Log.FileMissing(logger, query.Id.Value);
            return new NotFoundError("track_audio", query.Id.ToString());
        }

        return new LocalAudio(path, AudioFormats.ContentTypeOf(path));
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1020, Level = LogLevel.Warning, Message = "Refused to stream track {TrackId}: its file is outside the library folders")]
        public static partial void OutsideLibrary(ILogger logger, Guid trackId);

        [LoggerMessage(EventId = 1021, Level = LogLevel.Warning, Message = "Track {TrackId} has a local file that is no longer on disk")]
        public static partial void FileMissing(ILogger logger, Guid trackId);
    }
}
