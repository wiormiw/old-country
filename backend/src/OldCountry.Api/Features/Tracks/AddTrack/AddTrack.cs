using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Common.Library;
using OldCountry.Api.Common.Errors;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Common.Validation;
using OldCountry.Api.Features.Tracks.GetTrack;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Features.Tracks.AddTrack;

/// <summary>Registers a track in the library: a local file, a YouTube Music video, or both.</summary>
public sealed record AddTrackRequest
{
    /// <summary>Track title, 1-300 characters.</summary>
    public required string Title { get; init; }

    /// <summary>Artist display name. An existing artist with the same name (ignoring case) is reused.</summary>
    public required string Artist { get; init; }

    /// <summary>Length in seconds, at most 24 hours.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>Absolute path of the audio file on the server, if it is stored locally.</summary>
    public string? LocalPath { get; init; }

    /// <summary>Size of the local file in bytes. Required with <see cref="LocalPath"/>.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Codec of the local file.</summary>
    public AudioCodec Codec { get; init; } = AudioCodec.Unknown;

    /// <summary>Bitrate of the local file in kbit/s, 0 when unknown.</summary>
    public int BitrateKbps { get; init; }

    /// <summary>Sample rate of the local file in Hz, 0 when unknown.</summary>
    public int SampleRateHz { get; init; }

    /// <summary>The 11-character YouTube Music video id, if linked.</summary>
    public string? YouTubeVideoId { get; init; }
}

internal sealed class AddTrackRequestValidator : AbstractValidator<AddTrackRequest>
{
    public AddTrackRequestValidator()
    {
        RuleFor(r => r.Title)
            .NotEmpty().WithMessage("Give the track a title.")
            .MaximumLength(Track.TitleMaxLength);

        RuleFor(r => r.Artist)
            .NotEmpty().WithMessage("Give the artist's name; use \"Unknown Artist\" if you don't know it.")
            .MaximumLength(Domain.Library.Artist.NameMaxLength);

        RuleFor(r => r.DurationSeconds)
            .InclusiveBetween(1, 24 * 60 * 60).WithMessage("Duration must be between 1 second and 24 hours.");

        // At least one source; reported under "source" like the domain's own rule.
        RuleFor(r => r)
            .Must(r => r.LocalPath is not null || r.YouTubeVideoId is not null)
            .WithMessage("A track needs a local file path, a YouTube video id, or both.")
            .OverridePropertyName("source");

        When(r => r.LocalPath is not null, () =>
        {
            RuleFor(r => r.LocalPath!)
                .MaximumLength(4096)
                .Must(Path.IsPathFullyQualified).WithMessage("The local path must be absolute, e.g. /srv/music/song.flac.");
            RuleFor(r => r.SizeBytes)
                .NotNull().WithMessage("Give the file size in bytes along with the local path.")
                .GreaterThan(0);
            RuleFor(r => r.BitrateKbps).InclusiveBetween(0, 10_000);
            RuleFor(r => r.SampleRateHz).InclusiveBetween(0, 768_000);
        });

        // Reuses the domain's definition of a valid id rather than restating it.
        RuleFor(r => r.YouTubeVideoId)
            .Must(id => Domain.Library.YouTubeVideoId.Create(id).Value is YouTubeVideoId)
            .When(r => r.YouTubeVideoId is not null)
            .WithMessage("A YouTube video id is the 11 characters after \"v=\" in the video's URL.");
    }
}

public sealed record AddTrackCommand(AddTrackRequest Request) : ICommand<AddTrackResult>;

public readonly union AddTrackResult(TrackResponse, ValidationError, ConflictError);

internal sealed partial class AddTrackHandler(OldCountryDbContext db, ArtistDirectory artists, ILogger<AddTrackHandler> logger)
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
            switch (Domain.Library.YouTubeVideoId.Create(request.YouTubeVideoId))
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

internal static class AddTrackEndpoint
{
    public static void Map(IEndpointRouteBuilder tracks) => tracks
        .MapPost("/", Handle)
        .Validate<AddTrackRequest>()
        .WithName("AddTrack")
        .WithSummary("Add a track to the library")
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<Results<CreatedAtRoute<TrackResponse>, ValidationProblem, ProblemHttpResult>> Handle(
        AddTrackRequest request, ICommandHandler<AddTrackCommand, AddTrackResult> handler, CancellationToken cancellationToken) =>
        await handler.Handle(new AddTrackCommand(request), cancellationToken) switch
        {
            TrackResponse track => TypedResults.CreatedAtRoute(track, GetTrackEndpoint.Name, new { id = track.Id }),
            ValidationError invalid => invalid.ToProblem(),
            ConflictError conflict => conflict.ToProblem(),
        };
}
