using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Features.Tracks.StreamTrack;

public sealed record StreamTrackQuery(TrackId Id) : IQuery<StreamTrackResult>;

/// <summary>A file on disk the player may read, and how to label it.</summary>
public sealed record LocalAudio(string Path, string ContentType);

public readonly union StreamTrackResult(LocalAudio, NotFoundError);

internal sealed partial class StreamTrackHandler(OldCountryDbContext db, IOptions<LibraryOptions> library, ILogger<StreamTrackHandler> logger)
    : IQueryHandler<StreamTrackQuery, StreamTrackResult>
{
    public async Task<StreamTrackResult> Handle(StreamTrackQuery query, CancellationToken cancellationToken)
    {
        var path = await db.Tracks.AsNoTracking()
            .Where(track => track.Id == query.Id && track.LocalFile != null)
            .Select(track => track.LocalFile!.Path)
            .SingleOrDefaultAsync(cancellationToken);
        if (path is null) return new NotFoundError("track_audio", query.Id.ToString());

        // Paths come from clients (POST /api/tracks) as well as scans. Only ever serve files that
        // live inside the library folders, or this endpoint would read any file on the server.
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

internal static class StreamTrackEndpoint
{
    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapGet("/{id:guid}/audio", Handle)
        .WithName("StreamTrack")
        .WithSummary("Stream a track's local audio file")
        .WithDescription("Supports HTTP range requests, so players can seek. 404 when the track has no playable local file.")
        .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> Handle(
        Guid id, IQueryHandler<StreamTrackQuery, StreamTrackResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new StreamTrackQuery(new TrackId(id)), cancellationToken) switch
        {
            LocalAudio audio => TypedResults.PhysicalFile(audio.Path, audio.ContentType, enableRangeProcessing: true),
            NotFoundError notFound => notFound.ToProblem(),
        };
}
