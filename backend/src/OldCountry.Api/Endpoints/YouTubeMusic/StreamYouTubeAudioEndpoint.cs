using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Api.Http;
using OldCountry.Application.Common.Library;
using OldCountry.Application.YouTubeMusic.StreamYouTubeAudio;
using OldCountry.Domain.Errors;
using OldCountry.Domain.YouTube;

namespace OldCountry.Api.Endpoints.YouTubeMusic;

internal static class StreamYouTubeAudioEndpoint
{
    public static void Map(IEndpointRouteBuilder youTube) => youTube
        .MapGet("/videos/{videoId}/audio", Handle)
        .WithName("StreamYouTubeAudio")
        .WithSummary("Play a YouTube video's audio")
        .WithDescription("The first request fetches the audio with yt-dlp (a few seconds); later ones, and range requests for seeking, are served from the cache. Prefer GET /api/tracks/{id}/audio when the track is available offline.")
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status502BadGateway);

    private static async Task<Results<PhysicalFileHttpResult, ValidationProblem, ProblemHttpResult>> Handle(
        string videoId, YouTubeAudioCache cache, CancellationToken cancellationToken)
    {
        if (YouTubeVideoId.Create(videoId).Value is not YouTubeVideoId id)
            return ValidationError.For("videoId", "A YouTube video id is 11 characters of letters, digits, '-' or '_'.").ToProblem();

        var (path, error) = await cache.GetAsync(id, cancellationToken);
        return path is not null
            ? TypedResults.PhysicalFile(path, ContentTypeOf(path), enableRangeProcessing: true)
            : error!.ToProblem();
    }

    /// <summary>The native YouTube containers first (WebM/Opus, M4A/AAC), then the formats of the library.</summary>
    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webm" => "audio/webm",
        _ => AudioFormats.ContentTypeOf(path),
    };
}
