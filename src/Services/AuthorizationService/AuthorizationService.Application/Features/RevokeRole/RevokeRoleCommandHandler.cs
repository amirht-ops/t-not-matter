using MediatR;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace AuthorizationService.Application.Features.RevokeRole;

public sealed class RevokeRoleCommandHandler(IRoleAssignmentRepository assignments, IAuthorizationAuditSink auditSink, IAuthorizationCache cache, IRequestContextAccessor requestContext) : IRequestHandler<RevokeRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(RevokeRoleCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var subjectId = SubjectId.From(command.SubjectId);

        var assignment = await assignments.GetActiveAsync(tenantId, subjectId, command.RoleId, trackChanges: true, cancellationToken);
        if (assignment is null) return Result<Unit>.Failure(GeneralErrors.NotFound);
        assignment.Revoke(context.CorrelationId);
        await assignments.UpdateAsync(assignment, cancellationToken);
        await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.role_revoked", context.TenantId, context.CorrelationId, command.SubjectId, true, command.RoleId.ToString(), cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
