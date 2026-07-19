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

namespace AuthorizationService.Application.Features.AssignRole;

public sealed class AssignRoleCommandHandler(IRoleRepository roles, IRoleAssignmentRepository assignments, IAuthorizationAuditSink auditSink, IAuthorizationCache cache, IRequestContextAccessor requestContext) : IRequestHandler<AssignRoleCommand, Result<AssignRoleResponse>>
{
    public async Task<Result<AssignRoleResponse>> Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var subjectId = SubjectId.From(command.SubjectId);

        var role = await roles.GetByIdAsync(tenantId, command.RoleId, cancellationToken: cancellationToken);
        if (role is null) return Result<AssignRoleResponse>.Failure(GeneralErrors.NotFound);

        if (role.Status != RoleStatus.Active)
            return Result<AssignRoleResponse>.Failure(AuthorizationErrors.RoleNotActive);

        var existingActive = await assignments.GetActiveForSubjectAsync(tenantId, subjectId, cancellationToken: cancellationToken);
        if (existingActive.Count > 0)
            return Result<AssignRoleResponse>.Failure(AuthorizationErrors.SubjectAlreadyHasRole);

        var assignment = RoleAssignment.Assign(tenantId, subjectId, RoleId.From(command.RoleId), role.DepartmentId, UserId.From(command.AssignedBy), context.CorrelationId);

        await assignments.AddAsync(assignment, cancellationToken);
        await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.role_assigned", context.TenantId, context.CorrelationId, command.SubjectId, true, command.RoleId.ToString(), cancellationToken);
        return Result<AssignRoleResponse>.Success(new AssignRoleResponse(assignment.Id, assignment.SubjectId.Value, assignment.RoleId.Value, assignment.AssignedAtUtc));
    }
}
