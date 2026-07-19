using FluentValidation;
using SharedKernel.Errors;
using SharedKernel.Responses;

namespace IdentityService.Api.Filters;

public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return await next(context);
        }

        var validationResult = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (validationResult.IsValid)
        {
            return await next(context);
        }

        var errors = validationResult.Errors
            .Select(error => new ValidationError(error.PropertyName, error.ErrorMessage))
            .ToArray();

        var correlationId = (Guid?)context.HttpContext.Items["CorrelationId"] ?? Guid.Empty;
        var response = ApiResponse<object>.Fail(
            new ApiError(GeneralErrors.Validation.Code, GeneralErrors.Validation.Description, errors),
            correlationId);

        return TypedResults.BadRequest(response);
    }
}
