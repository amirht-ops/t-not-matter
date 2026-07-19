using SharedKernel.Errors;

namespace SharedKernel.Responses;

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyCollection<ValidationError>? ValidationErrors = null);