using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Aggregates.Permission;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.CreatePermission;

public sealed class CreatePermissionCommandHandler(IPermissionRepository permissions, IAuthorizationAuditSink auditSink, IRequestContextAccessor requestContext) : IRequestHandler<CreatePermissionCommand, Result<CreatePermissionResponse>>
{
    public async Task<Result<CreatePermissionResponse>> Handle(CreatePermissionCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var keyResult = PermissionKey.Create(command.Key);
        if (keyResult.IsFailure) return Result<CreatePermissionResponse>.Failure(keyResult.Error);

        var key = keyResult.Value!;
        if (await permissions.ExistsByKeyAsync(tenantId, key, cancellationToken)) return Result<CreatePermissionResponse>.Failure(GeneralErrors.Conflict);

        var permissionResult = Permission.Create(tenantId, key, AuthorizationAction.Create(command.Action), command.ResourceType, command.Description, "Unassigned", context.CorrelationId);
        if (permissionResult.IsFailure) return Result<CreatePermissionResponse>.Failure(permissionResult.Error);

        var permission = permissionResult.Value!;

        // Onboarding provisioning creates a permission that must be immediately grantable.
        // Drive it through the existing domain lifecycle (Draft -> Review -> Approved -> Published)
        // so it lands in the Published state that GrantPermission requires. Each transition
        // raises its lifecycle domain event, preserving the audit/outbox trail.
        var reviewResult = permission.SubmitForReview(context.CorrelationId);
        if (reviewResult.IsFailure) return Result<CreatePermissionResponse>.Failure(reviewResult.Error);

        var approveResult = permission.Approve(context.CorrelationId);
        if (approveResult.IsFailure) return Result<CreatePermissionResponse>.Failure(approveResult.Error);

        var publishResult = permission.Publish(context.CorrelationId);
        if (publishResult.IsFailure) return Result<CreatePermissionResponse>.Failure(publishResult.Error);

        await permissions.AddAsync(permission, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.permission_created", context.TenantId, context.CorrelationId, context.UserId, true, permission.Key.Value, cancellationToken);
        return Result<CreatePermissionResponse>.Success(new CreatePermissionResponse(permission.Id, permission.Key.Value, permission.Action.Value, permission.ResourceType));
    }
}
