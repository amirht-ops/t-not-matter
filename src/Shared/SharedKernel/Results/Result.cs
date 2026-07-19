using System.Text.Json.Serialization;
using SharedKernel.Errors;

namespace SharedKernel.Results;

public sealed class Result<T>
{
    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = Error.None;
        ValidationErrors = Array.Empty<ValidationError>();
    }

    private Result(Error error)
    {
        IsSuccess = false;
        Value = default;
        Error = error;
        ValidationErrors = Array.Empty<ValidationError>();
    }

    private Result(IReadOnlyCollection<ValidationError> validationErrors)
    {
        IsSuccess = false;
        Value = default;
        Error = GeneralErrors.Validation;
        ValidationErrors = validationErrors;
    }

    [JsonConstructor]
    private Result(bool isSuccess, T? value, Error? error, IReadOnlyCollection<ValidationError>? validationErrors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error ?? Error.None;
        ValidationErrors = validationErrors ?? Array.Empty<ValidationError>();
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error Error { get; }
    public IReadOnlyCollection<ValidationError> ValidationErrors { get; }

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Result<T>(value);
    }

    public static Result<T> Failure(Error error)
    {
        if (error == Error.None || error.Type == ErrorType.None)
        {
            throw new ArgumentException("Failure result requires a non-empty error.", nameof(error));
        }

        return new Result<T>(error);
    }

    public static Result<T> ValidationFailure(IReadOnlyCollection<ValidationError> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(validationErrors);

        return new Result<T>(validationErrors);
    }
}
