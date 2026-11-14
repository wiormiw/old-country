using Microsoft.AspNetCore.Http.HttpResults;
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

    public static ProblemHttpResult ToProblem(this ExternalServiceError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "A service this depends on failed.",
        detail: error.Message,
        extensions: Code(error));

    public static ValidationProblem ToProblem(this ValidationError error) => TypedResults.ValidationProblem(
        errors: error.Errors,
        title: "One or more validation errors occurred.",
        extensions: Code(error));

    private static Dictionary<string, object?> Code(IError error) => new() { ["code"] = error.Code };
}
