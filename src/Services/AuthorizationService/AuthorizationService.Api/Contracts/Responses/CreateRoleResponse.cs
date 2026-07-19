namespace AuthorizationService.Api.Contracts.Responses;

public sealed record CreateRoleResponse(Guid RoleId, string Name, string Status);
