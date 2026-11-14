using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Abstractions.YouTube;
using OldCountry.Application.Common.Library;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Tracks;

namespace OldCountry.Application.Downloads.DownloadTrack;

internal sealed partial class DownloadTrackHandler(
    IYtDlp ytDlp,
    IAppDbContext db,
    ArtistDirectory artists,
    TimeProvider clock,
    IOptions<YouTubeMusicOptions> youTube,
    IOptions<LibraryOptions> library,
    ILogger<DownloadTrackHandler> logger) : ICommandHandler<DownloadTrackCommand, DownloadTrackResult>
{
    private const string UnknownArtist = "Unknown Artist";

    public async Task<DownloadTrackResult> Handle(DownloadTrackCommand command, CancellationToken cancellationToken)
    {
        var resolved = youTube.Value.ResolveDownloadFolder(library.Value);
        if (resolved is ValidationError noFolder) return noFolder;
        var folder = (string)resolved.Value!;

        var existing = await db.Tracks.SingleOrDefaultAsync(t => t.YouTubeVideoId == command.VideoId, cancellationToken);
        if (existing is { IsAvailableOffline: true }) return existing.Id;

        YtDownload download;
        try
        {
            download = await ytDlp.DownloadAudioAsync(command.VideoId, folder, cancellationToken);
        }
        catch (YtDlpException exception)
        {
            // Keep the reason on the track. The app can show it, and "download all" can skip the track.
            if (existing is not null && YouTubeRefusals.RefusalOf(exception.Code) is { } refusal)
            {
                existing.RecordDownloadProblem(DownloadProblem.Of(refusal, exception.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(cancellationToken);
            }
            return exception.ToError();
        }

        var quality = new AudioQuality(AudioFormats.CodecOf(download.Path), download.BitrateKbps, download.SampleRateHz);
        if (LocalFile.Create(download.Path, new FileInfo(download.Path).Length, quality).Value is not LocalFile file)
            return new ExternalServiceError("youtube.bad_download", "yt-dlp reported a file that can't be added to the library.");

        Track track;
        if (existing is not null)
        {
            existing.AttachLocalFile(file);
            track = existing;
        }
        else
        {
            var video = download.Video;
            var artist = await artists.FindOrCreateAsync(Clip(video.Artist ?? UnknownArtist, Artist.NameMaxLength), cancellationToken);
            var created = Track.Create(Clip(video.Title, Track.TitleMaxLength), artist.Id, video.Duration ?? TimeSpan.Zero, file, command.VideoId);
            if (created is ValidationError invalid) return invalid;
            track = (Track)created.Value!;
            db.Tracks.Add(track);
        }

        await db.SaveChangesAsync(cancellationToken);
        Log.Downloaded(logger, command.VideoId.Value, track.Id.Value);
        return track.Id;
    }

    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static partial class Log
    {
        [LoggerMessage(EventId = 1320, Level = LogLevel.Information, Message = "Downloaded {VideoId} as track {TrackId}")]
        public static partial void Downloaded(ILogger logger, string videoId, Guid trackId);
    }
}
