using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Http;

/// <summary>
/// The one place where unexpected exceptions become HTTP responses.
/// Expected results do not come here: the use cases return them as union results.
/// This handler converts a broken invariant (409), a request that it cannot read (400), and a client that disconnected (499).
/// All other exceptions go to the 500 response of the framework.
/// </summary>
internal sealed partial class ProblemDetailsExceptionHandler(IProblemDetailsService problems, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected, so no one reads the response. Nginx uses 499 for this.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        ProblemDetails? problem = exception switch
        {
            BadHttpRequestException { InnerException: JsonException json } => new ValidationProblemDetails(
                new Dictionary<string, string[]> { [JsonPath(json)] = ["The JSON is malformed or has the wrong type here."] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request body could not be read.",
            },
            BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Title = "Bad request.", Detail = bad.Message },
            DomainException domain => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict.", Detail = domain.Message },
            _ => null,
        };
        if (problem is null) return false;

        problem.Extensions["code"] = exception is DomainException d ? d.Code : "bad_request";
        if (exception is DomainException invariant) InvariantBroken(logger, invariant, invariant.Code);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    /// <summary>Changes <c>$.title</c> to <c>title</c>, the key that the form of the client uses.</summary>
    private static string JsonPath(JsonException json) =>
        json.Path is { Length: > 2 } path && path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : "body";

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "Domain invariant {ErrorCode} was broken")]
    private static partial void InvariantBroken(ILogger logger, Exception exception, string errorCode);
}
