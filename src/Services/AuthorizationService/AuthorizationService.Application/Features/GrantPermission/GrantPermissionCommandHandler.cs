using MediatR;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Aggregates.Permission;
using AuthorizationService.Domain.Aggregates.PermissionGrant;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.GrantPermission;

public sealed class GrantPermissionCommandHandler(IRoleRepository roles, IPermissionRepository permissions, IAuthorizationAuditSink auditSink, IRequestContextAccessor requestContext) : IRequestHandler<GrantPermissionCommand, Result<GrantPermissionResponse>>
{
    public async Task<Result<GrantPermissionResponse>> Handle(GrantPermissionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;

        var role = await roles.GetByIdAsync(tenantId, command.RoleId, cancellationToken: cancellationToken);
        var permission = await permissions.GetByIdAsync(tenantId, command.PermissionId, cancellationToken: cancellationToken);
        if (role is null || permission is null) return Result<GrantPermissionResponse>.Failure(GeneralErrors.NotFound);

        if (permission.Lifecycle != PermissionLifecycle.Published)
            return Result<GrantPermissionResponse>.Failure(AuthorizationService.Domain.Errors.AuthorizationErrors.PermissionNotPublished);

        var existing = await permissions.GetActiveGrantAsync(tenantId, command.RoleId, command.PermissionId, cancellationToken: cancellationToken);
        if (existing is not null) return Result<GrantPermissionResponse>.Failure(GeneralErrors.Conflict);

        var grant = PermissionGrant.Grant(tenantId, RoleId.From(command.RoleId), PermissionId.From(command.PermissionId), UserId.From(command.GrantedBy), context.CorrelationId);
        await permissions.AddGrantAsync(grant, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.permission_granted", context.TenantId, context.CorrelationId, context.UserId, true, command.PermissionId.ToString(), cancellationToken);
        return Result<GrantPermissionResponse>.Success(new GrantPermissionResponse(grant.Id, grant.RoleId.Value, grant.PermissionId.Value, grant.GrantedAtUtc));
    }
}
