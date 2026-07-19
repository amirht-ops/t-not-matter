using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using TenantService.Api.Extensions;
using TenantService.Application.Features.ListDepartments;
using TenantService.Application.Features.CreateDepartment;
using TenantService.Application.Features.DeleteDepartment;
using TenantService.Application.Features.GetDepartmentById;
using TenantService.Application.Features.UpdateDepartment;
using SharedKernel.Responses;

namespace TenantService.Api.Endpoints;

public static class DepartmentEndpoints
{
    public static IEndpointRouteBuilder MapDepartmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenants/{tenantId:guid}/departments")
            .WithTags("Department Management")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/", CreateDepartmentAsync)
            .WithName("CreateDepartment")
            .WithSummary("Create a new department")
            .Produces<ApiResponse<CreateDepartmentResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        group.MapGet("/", ListDepartmentsAsync)
            .WithName("ListDepartments")
            .WithSummary("List all departments for a tenant")
            .Produces<ApiResponse<IReadOnlyList<DepartmentDto>>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetDepartmentByIdAsync)
            .WithName("GetDepartmentById")
            .WithSummary("Get department by ID for validation")
            .Produces<ApiResponse<DepartmentDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", UpdateDepartmentAsync)
            .WithName("UpdateDepartment")
            .WithSummary("Update department name or description")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", DeleteDepartmentAsync)
            .WithName("DeleteDepartment")
            .WithSummary("Deactivate a department")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async ValueTask<IResult> CreateDepartmentAsync(
        Guid tenantId,
        CreateDepartmentRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new CreateDepartmentCommand(tenantId, request.Name, request.Description, correlationId);
        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Created(
            $"/api/v1/tenants/{tenantId}/departments/{result.Value!.DepartmentId}",
            ApiResponse<CreateDepartmentResponse>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> ListDepartmentsAsync(
        Guid tenantId,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var query = new ListDepartmentsQuery(tenantId);
        var result = await mediator.Send(query, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<IReadOnlyList<DepartmentDto>>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> GetDepartmentByIdAsync(
        Guid tenantId,
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var query = new GetDepartmentByIdQuery(tenantId, id);
        var result = await mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Ok(ApiResponse<DepartmentDto>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> UpdateDepartmentAsync(
        Guid tenantId,
        Guid id,
        UpdateDepartmentRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new UpdateDepartmentCommand(tenantId, id, request.Name, request.Description, correlationId);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<Unit>.Ok(Unit.Value, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> DeleteDepartmentAsync(
        Guid tenantId,
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new DeleteDepartmentCommand(tenantId, id, correlationId);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.ToHttpResult(correlationId);
    }

    public sealed record CreateDepartmentRequest(string Name, string? Description);

    public sealed record UpdateDepartmentRequest(string? Name, string? Description);
}
