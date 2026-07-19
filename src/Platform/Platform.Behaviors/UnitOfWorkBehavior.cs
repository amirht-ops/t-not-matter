using MediatR;
using Platform.Abstractions.Infrastructure;
using SharedKernel.Application;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace Platform.Behaviors;

public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ITransactionalRequest)
            return await next();

        var transactional = unitOfWork as ITransactionalUnitOfWork;

        if (transactional is not null)
            await transactional.BeginTransactionAsync(ct);

        try
        {
            var response = await next();
            var isSuccess = IsSuccessResponse(response);

            if (isSuccess)
                {
                    try
                    {
                        await unitOfWork.SaveChangesAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        if (transactional is not null)
                        {
                            await transactional.RollbackTransactionAsync(ct);
                            transactional.ClearChangeTracker();
                        }

                        if (ex.GetType().Name == "DbUpdateConcurrencyException")
                            throw;

                        if (IsTransientException(ex))
                            throw;

                        return CreateFailureResponse(ex);
                    }
                }

            if (transactional is not null)
            {
                if (isSuccess)
                    await transactional.CommitTransactionAsync(ct);
                else
                    await transactional.RollbackTransactionAsync(ct);
            }

            return response;
        }
        catch
        {
            if (transactional is not null)
            {
                await transactional.RollbackTransactionAsync(ct);
                transactional.ClearChangeTracker();
            }
            throw;
        }
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex switch
        {
            TimeoutException => true,
            HttpRequestException => true,
            TaskCanceledException tce => !tce.CancellationToken.IsCancellationRequested,
            _ => false
        };
    }

    private static bool IsSuccessResponse(TResponse response)
    {
        if (response is null)
            return false;

        var responseType = response.GetType();
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var isSuccessProp = responseType.GetProperty("IsSuccess");
            return isSuccessProp is not null && (bool)isSuccessProp.GetValue(response)!;
        }

        return true;
    }

    private static TResponse CreateFailureResponse(Exception ex)
    {
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var resultType = typeof(TResponse).GetGenericArguments()[0];
            var error = ex.GetType().Name == "DbUpdateConcurrencyException"
                ? Error.Conflict("ConcurrencyConflict", "The record was modified by another request. Reload and retry.")
                : Error.Failure("PersistenceError", $"Failed to persist changes: {ex.Message}");

            var failureMethod = typeof(Result<>).MakeGenericType(resultType)
                .GetMethod("Failure", [typeof(Error)]);
            return (TResponse)failureMethod!.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"Persistence failure for {typeof(TResponse).Name}: {ex.Message}", ex);
    }
}
