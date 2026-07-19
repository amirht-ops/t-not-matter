using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.Consumption;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class ConsumptionEndpoints
{
    public static IEndpointRouteBuilder MapConsumptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/consumption")
            .WithTags("Consumption")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/record", RecordConsumptionAsync)
            .WithName("RecordConsumption")
            .WithSummary("Record feed consumption for a consumer")
            .Produces<ApiResponse<RecordConsumptionResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/{consumerId:guid}/allowance", GetAllowanceStatusAsync)
            .WithName("GetAllowanceStatus")
            .WithSummary("Get composite allowance status for a consumer/action")
            .Produces<ApiResponse<AllowanceStatusDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async ValueTask<IResult> RecordConsumptionAsync(
        RecordConsumptionRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new RecordConsumptionCommand(
            request.ConsumerId, request.ActionKey, request.Units, request.IdempotencyKey);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<RecordConsumptionResponse>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> GetAllowanceStatusAsync(
        Guid consumerId,
        string actionKey,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new GetAllowanceStatusQuery(consumerId, actionKey), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<AllowanceStatusDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    public sealed record RecordConsumptionRequest(Guid ConsumerId, string ActionKey, long Units, string IdempotencyKey);
}
