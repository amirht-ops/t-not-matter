using MediatR;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.Aggregates.RoleAssignment;
using AuthorizationService.Domain.Errors;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.ChangeUserRole;

public sealed class ChangeUserRoleCommandHandler(
    IRoleRepository roles,
    IRoleAssignmentRepository assignments,
    IAuthorizationAuditSink auditSink,
    IAuthorizationCache cache,
    IRequestContextAccessor requestContext) : IRequestHandler<ChangeUserRoleCommand, Result<ChangeUserRoleResponse>>
{
    public async Task<Result<ChangeUserRoleResponse>> Handle(ChangeUserRoleCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var subjectId = SubjectId.From(command.SubjectId);
        var correlationId = context.CorrelationId;

        var activeAssignments = await assignments.GetActiveForSubjectAsync(tenantId, subjectId, trackChanges: true, cancellationToken: cancellationToken);
        var currentAssignment = activeAssignments.FirstOrDefault();
        if (currentAssignment is null)
            return Result<ChangeUserRoleResponse>.Failure(AuthorizationErrors.SubjectHasNoActiveRoleAssignment);

        var targetRole = await roles.GetByIdAsync(tenantId, command.NewRoleId, cancellationToken: cancellationToken);
        if (targetRole is null)
            return Result<ChangeUserRoleResponse>.Failure(GeneralErrors.NotFound);

        if (targetRole.Status != RoleStatus.Active)
            return Result<ChangeUserRoleResponse>.Failure(AuthorizationErrors.RoleNotActive);

        var currentRole = await roles.GetByIdAsync(tenantId, currentAssignment.RoleId.Value, cancellationToken: cancellationToken);
        if (currentRole is null)
            return Result<ChangeUserRoleResponse>.Failure(AuthorizationErrors.SubjectHasNoActiveRoleAssignment);

        if (currentAssignment.RoleId.Value == command.NewRoleId)
            return Result<ChangeUserRoleResponse>.Success(new ChangeUserRoleResponse(
                currentAssignment.Id, currentAssignment.Id, subjectId.Value, currentAssignment.RoleId.Value, command.NewRoleId, currentAssignment.AssignedAtUtc));

        currentAssignment.Revoke(correlationId);

        var newAssignment = RoleAssignment.Assign(tenantId, subjectId, RoleId.From(command.NewRoleId), targetRole.DepartmentId, UserId.From(command.AssignedBy), correlationId);
        await assignments.AddAsync(newAssignment, cancellationToken);

        await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        await auditSink.RecordStateChangeAsync(
            "AuthorizationService.role_changed",
            tenantId, correlationId, command.SubjectId, true,
            $"{command.NewRoleId:N}", cancellationToken);

        return Result<ChangeUserRoleResponse>.Success(new ChangeUserRoleResponse(
            currentAssignment.Id, newAssignment.Id, subjectId.Value, currentAssignment.RoleId.Value, command.NewRoleId, newAssignment.AssignedAtUtc));
    }
}
