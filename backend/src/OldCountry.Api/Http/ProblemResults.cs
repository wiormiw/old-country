using Microsoft.AspNetCore.Http.HttpResults;
using OldCountry.Application.Common.YouTube;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Http;

/// <summary>
/// Changes the error records of the domain into RFC 9457 problem details.
/// Each problem has a stable <c>code</c> extension that clients (the apps) can check.
/// </summary>
internal static class ProblemResults
{
    public static ProblemHttpResult ToProblem(this NotFoundError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found.",
        detail: $"No {error.Resource} has id '{error.Id}'.",
        extensions: Code(error));

    public static ProblemHttpResult ToProblem(this ConflictError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Conflict.",
        detail: error.Message,
        extensions: Code(error));

    /// <summary>
    /// A refusal of one video by YouTube is a normal answer about that video, not a failure of the server.
    /// Thus it gets a 4xx status. Only real failures (yt-dlp failed, a timeout) get a 5xx status.
    /// The 5xx metrics and alerts then show only the problems of the server. The <c>code</c> does not change.
    /// </summary>
    public static ProblemHttpResult ToProblem(this ExternalServiceError error)
    {
        var (status, title) = YouTubeRefusals.RefusalOf(error.Code) switch
        {
            DownloadRefusal.Unavailable => (StatusCodes.Status404NotFound, "YouTube does not have this video."),
            DownloadRefusal.PremiumOnly or DownloadRefusal.Private or DownloadRefusal.AgeRestricted or DownloadRefusal.RegionBlocked
                => (StatusCodes.Status403Forbidden, "YouTube refuses this video."),
            _ when error.Code == YouTubeRefusals.RateLimited
                => (StatusCodes.Status503ServiceUnavailable, "YouTube asked the server to slow down. Try again later."),
            _ => (StatusCodes.Status502BadGateway, "A service this depends on failed."),
        };
        return TypedResults.Problem(statusCode: status, title: title, detail: error.Message, extensions: Code(error));
    }

    /// <summary>The problem statuses of an endpoint that asks YouTube about one video: refused, gone, rate limited, failed.</summary>
    public static RouteHandlerBuilder ProducesYouTubeProblems(this RouteHandlerBuilder endpoint) => endpoint
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    public static ValidationProblem ToProblem(this ValidationError error) => TypedResults.ValidationProblem(
        errors: error.Errors,
        title: "One or more validation errors occurred.",
        extensions: Code(error));

    private static Dictionary<string, object?> Code(IError error) => new() { ["code"] = error.Code };
}
