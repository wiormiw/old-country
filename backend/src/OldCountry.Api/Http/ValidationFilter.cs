using FluentValidation;
using OldCountry.Application.Pipeline.Validation;

namespace OldCountry.Api.Http;

/// <summary>Runs the validator of the request before the endpoint. A request that is not valid gets 400 with the field errors.</summary>
internal sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Binding is complete at this point. The framework already refused a missing body.
        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
            return await next(context);

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        return result.IsValid ? await next(context) : result.ToValidationError().ToProblem();
    }
}
