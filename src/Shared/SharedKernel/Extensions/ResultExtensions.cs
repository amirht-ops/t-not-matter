using SharedKernel.Responses;
using SharedKernel.Results;

namespace SharedKernel.Extensions;

public static class ResultExtensions
{
    public static ApiResponse<T> ToApiResponse<T>(this Result<T> result, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return ApiResponse<T>.Ok(result.Value!, correlationId);
        }

        var validationErrors = result.ValidationErrors.Count > 0
            ? result.ValidationErrors
            : null;

        var apiError = new ApiError(
            result.Error.Code,
            result.Error.Description,
            validationErrors);

        return ApiResponse<T>.Fail(apiError, correlationId);
    }
}