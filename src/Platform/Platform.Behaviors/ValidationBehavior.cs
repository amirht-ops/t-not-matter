using FluentValidation;
using MediatR;
using SharedKernel.Results;
using SharedKernel.Errors;

namespace Platform.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var error = Error.Validation("ValidationError", "One or more validation errors occurred");
            var resultType = typeof(TResponse).GetGenericArguments()[0];
            var failureMethod = typeof(Result<>).MakeGenericType(resultType)
                .GetMethod("ValidationFailure", [typeof(IReadOnlyCollection<ValidationError>)]);
            var validationErrors = failures.Select(f => new ValidationError(f.PropertyName, f.ErrorMessage)).ToList();
            return (TResponse)failureMethod!.Invoke(null, [validationErrors])!;
        }

        throw new ValidationException(failures);
    }
}
