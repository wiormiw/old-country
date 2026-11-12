using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Middleware;

/// <summary>
/// The one place unexpected exceptions become HTTP. Expected outcomes never get here: use cases
/// return them as union results. What's left is broken invariants (409), unreadable requests
/// (400), and a client that hung up (499). Anything else falls through to the framework's 500.
/// </summary>
internal sealed partial class ProblemDetailsExceptionHandler(IProblemDetailsService problems, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client left; nobody is reading the answer. 499 is nginx's name for that.
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

    /// <summary><c>$.title</c> → <c>title</c>, the key the client's form would use.</summary>
    private static string JsonPath(JsonException json) =>
        json.Path is { Length: > 2 } path && path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : "body";

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "Domain invariant {ErrorCode} was broken")]
    private static partial void InvariantBroken(ILogger logger, Exception exception, string errorCode);
}
