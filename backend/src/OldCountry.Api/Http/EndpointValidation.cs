namespace OldCountry.Api.Http;

/// <summary>Adds request validation to an endpoint.</summary>
internal static class EndpointValidation
{
    /// <summary>Runs the validator of <typeparamref name="TRequest"/> before the endpoint handler.</summary>
    public static RouteHandlerBuilder Validate<TRequest>(this RouteHandlerBuilder endpoint) => endpoint
        .AddEndpointFilter<ValidationFilter<TRequest>>()
        .ProducesValidationProblem();
}
