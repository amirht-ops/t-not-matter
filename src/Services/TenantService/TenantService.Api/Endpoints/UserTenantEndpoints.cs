using MediatR;
using Platform.Abstractions.Principal;
using Platform.Abstractions.Tenant;
using TenantService.Api.Extensions;
using TenantService.Application.Features.GetCurrentTenant;
using TenantService.Application.Features.UpdateTenantSettings;
using SharedKernel.Responses;

namespace TenantService.Api.Endpoints;

public static class UserTenantEndpoints
{
    public static IEndpointRouteBuilder MapUserTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenants")
            .WithTags("Tenant Profile (User)")
            .RequireAuthorization();

        group.MapGet("/me/tenant", GetMyTenantAsync)
            .WithName("GetMyTenant")
            .WithSummary("Get the current tenant for the authenticated user")
            .Produces<ApiResponse<CurrentTenantDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/profile", GetTenantProfileAsync)
            .WithName("GetTenantProfile")
            .WithSummary("Get the full profile of the current tenant")
            .Produces<ApiResponse<CurrentTenantDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPut("/settings", UpdateTenantSettingsAsync)
            .WithName("UpdateTenantSettings")
            .WithSummary("Update tenant settings (name)")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async ValueTask<IResult> GetMyTenantAsync(
        IMediator mediator,
        ICurrentPrincipalAccessor principalAccessor,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        var tenantId = principal.TenantId;

        if (tenantId is null || tenantId.Value == Guid.Empty)
            return Results.Unauthorized();

        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(
            new GetCurrentTenantQuery(tenantId.Value, correlationId), cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Ok(ApiResponse<CurrentTenantDto>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> GetTenantProfileAsync(
        IMediator mediator,
        ICurrentPrincipalAccessor principalAccessor,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        var tenantId = principal.TenantId;

        if (tenantId is null || tenantId.Value == Guid.Empty)
            return Results.Unauthorized();

        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(
            new GetCurrentTenantQuery(tenantId.Value, correlationId), cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Ok(ApiResponse<CurrentTenantDto>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> UpdateTenantSettingsAsync(
        UpdateTenantSettingsRequest request,
        IMediator mediator,
        ICurrentPrincipalAccessor principalAccessor,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        var tenantId = principal.TenantId;

        if (tenantId is null || tenantId.Value == Guid.Empty)
            return Results.Unauthorized();

        var correlationId = accessor.Context.CorrelationId;
        var command = new UpdateTenantSettingsCommand(tenantId.Value, request.Name, correlationId);
        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Ok(ApiResponse<Unit>.Ok(Unit.Value, correlationId));
    }

    public sealed record UpdateTenantSettingsRequest(string Name);
}