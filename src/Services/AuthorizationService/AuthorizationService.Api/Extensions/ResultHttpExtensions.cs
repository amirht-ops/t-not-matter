using SharedKernel.Errors;
using SharedKernel.Extensions;
using SharedKernel.Responses;
using SharedKernel.Results;

namespace AuthorizationService.Api.Extensions;

public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Guid correlationId) =>
        result.Error.Type switch
        {
            ErrorType.Validation => TypedResults.BadRequest(result.ToApiResponse(correlationId)),
            ErrorType.Unauthorized => TypedResults.Json(result.ToApiResponse(correlationId),
                statusCode: StatusCodes.Status401Unauthorized),
            ErrorType.Forbidden => TypedResults.Json(result.ToApiResponse(correlationId),
                statusCode: StatusCodes.Status403Forbidden),
            ErrorType.NotFound => TypedResults.NotFound(result.ToApiResponse(correlationId)),
            ErrorType.Conflict => TypedResults.Conflict(result.ToApiResponse(correlationId)),
            _ when result.IsSuccess => TypedResults.Ok(result.ToApiResponse(correlationId)),
            _ => TypedResults.Json(
                result.ToApiResponse(correlationId),
                statusCode: StatusCodes.Status500InternalServerError)
        };

    public static IResult ToOkHttpResult<T>(T response, Guid correlationId) =>
        TypedResults.Ok(ApiResponse<T>.Ok(response, correlationId));
}
