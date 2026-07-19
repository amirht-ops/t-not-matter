using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.PrincipalHierarchy;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class PrincipalHierarchyEndpoints
{
    public static IEndpointRouteBuilder MapPrincipalHierarchyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/principal-hierarchy")
            .WithTags("Principal Hierarchy")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/edges", UpsertPrincipalEdgeAsync)
            .WithName("UpsertPrincipalEdge")
            .WithSummary("Upsert a principal hierarchy node or edge")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async ValueTask<IResult> UpsertPrincipalEdgeAsync(
        UpsertPrincipalEdgeRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new UpsertPrincipalEdgeCommand(
            request.Operation, request.NodeId, request.UserId, request.RoleId);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    public sealed record UpsertPrincipalEdgeRequest(
        PrincipalHierarchyOperation Operation,
        Guid? NodeId = null,
        Guid? UserId = null,
        Guid? RoleId = null);
}
