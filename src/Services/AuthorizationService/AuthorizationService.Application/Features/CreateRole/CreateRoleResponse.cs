namespace AuthorizationService.Application.Features.CreateRole;

public sealed record CreateRoleResponse(Guid RoleId, string Name, string Status);
