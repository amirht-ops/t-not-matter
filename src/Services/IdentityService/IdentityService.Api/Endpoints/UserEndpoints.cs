using IdentityService.Api.Extensions;
using IdentityService.Application.Common.Abstractions;
using IdentityService.Application.Features.GetUser;
using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Responses;

namespace IdentityService.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity/users").WithTags("Identity Users").RequireAuthorization();

        group.MapGet("/{userId:guid}", GetUserAsync);

        return app;
    }

    private static async ValueTask<IResult> GetUserAsync(
        Guid userId,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new GetUserQuery(userId), cancellationToken);
        await auditSink.RecordAsync("IdentityService.user_fetched", context.TenantId, context.CorrelationId, userId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<GetUserResponse>.Ok(result.Value!, context.CorrelationId))
            : result.ToHttpResult(context.CorrelationId);
    }
}
