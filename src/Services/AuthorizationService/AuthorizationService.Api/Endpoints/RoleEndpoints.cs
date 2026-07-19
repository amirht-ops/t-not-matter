using AuthorizationService.Api.Contracts.Requests;
using AuthorizationService.Api.Extensions;
using AuthorizationService.Application.Features.AssignRole;
using AuthorizationService.Application.Features.ChangeUserRole;
using AuthorizationService.Application.Features.CreateRole;
using AuthorizationService.Application.Features.RevokeRole;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using SharedKernel.Responses;

namespace AuthorizationService.Api.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/authorization/roles")
            .WithTags("Authorization Roles")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);
        group.MapPost("/", CreateAsync);
        group.MapPost("/assignments", AssignAsync);
        group.MapPost("/assignments/revoke", RevokeAsync);
        group.MapPost("/assignments/change", ChangeAsync);
        group.MapGet("/assignment", GetActiveAssignmentAsync);
        return app;
    }

    private static async ValueTask<IResult> CreateAsync(CreateRoleRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new CreateRoleCommand(request.Name, request.Description, request.ParentRoleId, request.DepartmentId), cancellationToken);
        return result.IsSuccess ? TypedResults.Created($"/api/v1/authorization/roles/{result.Value!.RoleId}", ApiResponse<Application.Features.CreateRole.CreateRoleResponse>.Ok(result.Value!, context.CorrelationId)) : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> AssignAsync(AssignRoleRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new AssignRoleCommand(request.SubjectId, request.RoleId, request.AssignedBy), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(ApiResponse<AssignRoleResponse>.Ok(result.Value!, context.CorrelationId)) : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> RevokeAsync(RevokeRoleRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new RevokeRoleCommand(request.SubjectId, request.RoleId), cancellationToken);
        return result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> ChangeAsync(ChangeUserRoleRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new ChangeUserRoleCommand(request.SubjectId, request.NewRoleId, request.AssignedBy), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(ApiResponse<ChangeUserRoleResponse>.Ok(result.Value!, context.CorrelationId)) : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> GetActiveAssignmentAsync(
        Guid subjectId,
        IRoleAssignmentRepository assignments,
        IRoleRepository roles,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var tenantId = accessor.Context.TenantId;
        if (tenantId == Guid.Empty)
            return Results.BadRequest();

        var activeAssignments = await assignments.GetActiveForSubjectAsync(tenantId, SubjectId.From(subjectId), cancellationToken: cancellationToken);
        var assignment = activeAssignments.FirstOrDefault();
        if (assignment is null)
            return Results.Ok(new ActiveRoleResponse(null, null));

        var role = await roles.GetByIdAsync(tenantId, assignment.RoleId.Value, cancellationToken: cancellationToken);
        if (role is null)
            return Results.Ok(new ActiveRoleResponse(null, null));

        return Results.Ok(new ActiveRoleResponse(role.Id, role.DepartmentId));
    }

    public sealed record ActiveRoleResponse(Guid? RoleId, Guid? DepartmentId);
}
