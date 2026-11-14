using Microsoft.EntityFrameworkCore;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Library;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;
using OldCountry.Domain.YouTube;

namespace OldCountry.Application.Tracks.AddTrack;

internal sealed partial class AddTrackHandler(IAppDbContext db, ArtistDirectory artists, ILogger<AddTrackHandler> logger)
    : ICommandHandler<AddTrackCommand, AddTrackResult>
{
    public async Task<AddTrackResult> Handle(AddTrackCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        LocalFile? localFile = null;
        if (request.LocalPath is not null)
        {
            var quality = new AudioQuality(request.Codec, request.BitrateKbps, request.SampleRateHz);
            switch (LocalFile.Create(request.LocalPath, request.SizeBytes ?? 0, quality))
            {
                case LocalFile file: localFile = file; break;
                case ValidationError error: return error;
            }
        }

        YouTubeVideoId? videoId = null;
        if (request.YouTubeVideoId is not null)
        {
            switch (Domain.YouTube.YouTubeVideoId.Create(request.YouTubeVideoId))
            {
                case YouTubeVideoId id: videoId = id; break;
                case ValidationError error: return error;
            }
        }

        if (localFile is not null && await db.Tracks.AnyAsync(t => t.LocalFile!.Path == localFile.Path, cancellationToken))
            return new ConflictError("track.duplicate_file", "A track with this local file is already in the library.");
        if (videoId is { } vid && await db.Tracks.AnyAsync(t => t.YouTubeVideoId == vid, cancellationToken))
            return new ConflictError("track.duplicate_video", "A track with this YouTube video id is already in the library.");

        var artist = await artists.FindOrCreateAsync(request.Artist, cancellationToken);

        return Track.Create(request.Title, artist.Id, TimeSpan.FromSeconds(request.DurationSeconds), localFile, videoId) switch
        {
            Track track => await Save(track, artist, cancellationToken),
            ValidationError error => error,
        };
    }

    private async Task<TrackResponse> Save(Track track, Artist artist, CancellationToken cancellationToken)
    {
        db.Tracks.Add(track);
        await db.SaveChangesAsync(cancellationToken);
        Log.TrackAdded(logger, track.Id.Value, artist.Id.Value, track.IsAvailableOffline);

        return track.ToResponse(artist.Name);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Added track {TrackId} by artist {ArtistId} (offline: {IsAvailableOffline})")]
        public static partial void TrackAdded(ILogger logger, Guid trackId, Guid artistId, bool isAvailableOffline);
    }
}
