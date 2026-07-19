using AuthorizationService.Api.Contracts.Requests;
using AuthorizationService.Api.Extensions;
using AuthorizationService.Application.Features.CreatePermission;
using AuthorizationService.Application.Features.GrantPermission;
using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using SharedKernel.Responses;

namespace AuthorizationService.Api.Endpoints;

public static class PermissionEndpoints
{
    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/authorization/permissions")
            .WithTags("Authorization Permissions")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);
        group.MapPost("/", CreateAsync);
        group.MapPost("/grants", GrantAsync);
        return app;
    }

    private static async ValueTask<IResult> CreateAsync(CreatePermissionRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new CreatePermissionCommand(request.Key, request.Action, request.ResourceType, request.Description), cancellationToken);
        return result.IsSuccess ? TypedResults.Created($"/api/v1/authorization/permissions/{result.Value!.PermissionId}", ApiResponse<CreatePermissionResponse>.Ok(result.Value!, context.CorrelationId)) : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> GrantAsync(GrantPermissionRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new GrantPermissionCommand(request.RoleId, request.PermissionId, request.GrantedBy), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(ApiResponse<GrantPermissionResponse>.Ok(result.Value!, context.CorrelationId)) : result.ToHttpResult(context.CorrelationId);
    }

    public sealed record CreatePermissionRequest(string Key, string Action, string ResourceType, string? Description);
}
