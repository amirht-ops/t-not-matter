using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.Administration;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/administration")
            .WithTags("Administration")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/allowance/reset", ResetAllowanceAsync)
            .WithName("ResetAllowance")
            .WithSummary("Administratively reset usage and debt for a consumer")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async ValueTask<IResult> ResetAllowanceAsync(
        ResetAllowanceRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new ResetAllowanceCommand(request.ConsumerId), cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    public sealed record ResetAllowanceRequest(Guid ConsumerId);
}
