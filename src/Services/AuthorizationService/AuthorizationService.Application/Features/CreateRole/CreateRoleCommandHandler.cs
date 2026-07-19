using MediatR;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.CreateRole;

public sealed class CreateRoleCommandHandler(
    IRoleRepository roles,
    ITenantServiceClient tenantService,
    IAuthorizationAuditSink auditSink,
    IRequestContextAccessor requestContext) : IRequestHandler<CreateRoleCommand, Result<CreateRoleResponse>>
{
    public async Task<Result<CreateRoleResponse>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var departmentId = command.DepartmentId;

        var departmentValid = await tenantService.ValidateDepartmentExistsAsync(tenantId, departmentId, cancellationToken);
        if (departmentValid.IsFailure)
            return Result<CreateRoleResponse>.Failure(departmentValid.Error);
        if (!departmentValid.Value)
            return Result<CreateRoleResponse>.Failure(GeneralErrors.NotFound);

        var name = RoleName.Create(command.Name);
        if (await roles.ExistsByNameAsync(tenantId, departmentId, name, cancellationToken)) return Result<CreateRoleResponse>.Failure(GeneralErrors.Conflict);

        var parentRoleId = command.ParentRoleId is not null ? RoleId.From(command.ParentRoleId.Value) : null;
        var roleResult = Role.Create(tenantId, departmentId, name, command.Description, parentRoleId, 0, null, context.CorrelationId);
        if (roleResult.IsFailure) return Result<CreateRoleResponse>.Failure(roleResult.Error);

        var role = roleResult.Value!;
        await roles.AddAsync(role, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.role_created", context.TenantId, context.CorrelationId, context.UserId, true, role.Name.Value, cancellationToken);
        return Result<CreateRoleResponse>.Success(new CreateRoleResponse(role.Id, role.Name.Value, role.Status.ToString()));
    }
}
