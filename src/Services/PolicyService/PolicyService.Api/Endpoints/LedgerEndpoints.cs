using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.Ledgers;
using PolicyService.Domain.Enums;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ledgers")
            .WithTags("Ledgers")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapGet("/usage", GetUsageAsync)
            .WithName("GetUsage")
            .WithSummary("Get usage count for a consumer/action/window")
            .Produces<ApiResponse<UsageDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapGet("/debt", GetDebtAsync)
            .WithName("GetDebt")
            .WithSummary("Get outstanding debt for a consumer")
            .Produces<ApiResponse<DebtSummaryDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async ValueTask<IResult> GetUsageAsync(
        Guid consumerId,
        string actionKey,
        QuotaWindow window,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new GetUsageQuery(consumerId, actionKey, window), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<UsageDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> GetDebtAsync(
        Guid consumerId,
        string? actionKey,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new GetDebtQuery(consumerId, actionKey), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<DebtSummaryDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }
}
