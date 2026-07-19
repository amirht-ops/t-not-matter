namespace SharedKernel.Responses;

public sealed record ApiResponse<T>
{
    public ApiResponse() { }

    private ApiResponse(bool success, T? data, ApiError? error, Guid correlationId)
    {
        Success = success;
        Data = data;
        Error = error;
        CorrelationId = correlationId;
    }

    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }
    public Guid CorrelationId { get; init; }

    public static ApiResponse<T> Ok(T data, Guid correlationId) =>
        new(true, data, null, correlationId);

    public static ApiResponse<T> Fail(ApiError error, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ApiResponse<T>(false, default, error, correlationId);
    }
}