using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using TenantService.Api.Extensions;
using TenantService.Application.Features.CreateTenant;
using TenantService.Application.Features.ChangeTenantStatus;
using TenantService.Application.Features.UpdateTenantPlan;
using TenantService.Application.Features.GetTenantBySlug;
using TenantService.Domain.Enums;
using TenantService.Domain.ValueObjects;
using SharedKernel.Responses;

namespace TenantService.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenants")
            .WithTags("Tenant Management")
            .RequireAuthorization(PlatformAuthorizationPolicies.AllowIdentityAndAuthorization);

        group.MapPost("/", CreateTenantAsync)
            .WithName("CreateTenant")
            .WithSummary("Create a new tenant")
            .Produces<ApiResponse<CreateTenantResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}/status", ChangeTenantStatusAsync)
            .WithName("ChangeTenantStatus")
            .WithSummary("Change tenant status")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden);

        group.MapPatch("/{id:guid}/plan", UpdateTenantPlanAsync)
            .WithName("UpdateTenantPlan")
            .WithSummary("Update tenant plan tier")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden);

        group.MapGet("/slug/{slug}", GetTenantBySlugAsync)
            .WithName("GetTenantBySlug")
            .WithSummary("Get tenant by slug")
            .RequireAuthorization(PlatformAuthorizationPolicies.AllowIdentityAndAuthorization)
            .Produces<ApiResponse<TenantSlugResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async ValueTask<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var command = new CreateTenantCommand(request.Name, request.Identifier, request.Slug, request.PlanTier, context.CorrelationId);
        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(context.CorrelationId);

        var response = new CreateTenantResponse(result.Value!);
        return TypedResults.Created($"/api/v1/tenants/{result.Value}",
            ApiResponse<CreateTenantResponse>.Ok(response, context.CorrelationId));
    }

    private static async ValueTask<IResult> ChangeTenantStatusAsync(
        Guid id,
        ChangeTenantStatusRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var command = new ChangeTenantStatusCommand(id, request.Status, context.CorrelationId);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> UpdateTenantPlanAsync(
        Guid id,
        UpdateTenantPlanRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var command = new UpdateTenantPlanCommand(id, request.PlanTier);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> GetTenantBySlugAsync(
        string slug,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;

        var result = await mediator.Send(
            new GetTenantBySlugQuery(slug, context.CorrelationId), cancellationToken);

        if (result.IsFailure)
            return Results.NotFound(ApiResponse<object>.Fail(
                new ApiError("Tenant.NotFound", "Tenant not found"), context.CorrelationId));

        var tenant = result.Value!;
        var response = new TenantSlugResponse(tenant.TenantId, tenant.Name, tenant.Slug);
        return Results.Ok(ApiResponse<TenantSlugResponse>.Ok(response, context.CorrelationId));
    }

    public sealed record CreateTenantRequest(
        string Name,
        string Identifier,
        string Slug,
        PlanTier PlanTier = PlanTier.Free);

    public sealed record CreateTenantResponse(Guid Id);

    public sealed record TenantSlugResponse(Guid TenantId, string Name, string Slug);

    public sealed record ChangeTenantStatusRequest(TenantStatus Status);

    public sealed record UpdateTenantPlanRequest(PlanTier PlanTier);
}
