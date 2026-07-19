using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Repositories;
using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace AuthorizationService.Application.Features.RevokePermission;

public sealed class RevokePermissionCommandHandler(IPermissionRepository permissions, IAuthorizationAuditSink auditSink, IRequestContextAccessor requestContext) : IRequestHandler<RevokePermissionCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(RevokePermissionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var grant = await permissions.GetActiveGrantAsync(tenantId, command.RoleId, command.PermissionId, trackChanges: true, cancellationToken);
        if (grant is null) return Result<Unit>.Failure(GeneralErrors.NotFound);
        grant.Revoke(context.CorrelationId);
        await permissions.UpdateGrantAsync(grant, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.permission_revoked", context.TenantId, context.CorrelationId, context.UserId, true, command.PermissionId.ToString(), cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
