using OldCountry.Api.Http;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Tests.Http;

/// <summary>A refusal of one video by YouTube is a 4xx answer. Only a real failure is a 5xx status.</summary>
public sealed class ProblemResultsTests
{
    [Theory]
    [InlineData("youtube.premium_only", 403)]
    [InlineData("youtube.private", 403)]
    [InlineData("youtube.age_restricted", 403)]
    [InlineData("youtube.region_blocked", 403)]
    [InlineData("youtube.unavailable", 404)]
    [InlineData("youtube.rate_limited", 503)]
    [InlineData("youtube.failed", 502)]
    [InlineData("youtube.timeout", 502)]
    [InlineData("youtube.bad_download", 502)]
    public void Youtube_errors_get_the_status_that_matches_their_reason(string code, int status)
    {
        var problem = new ExternalServiceError(code, "message").ToProblem();

        problem.StatusCode.ShouldBe(status);
        problem.ProblemDetails.Extensions["code"].ShouldBe(code);
    }
}
