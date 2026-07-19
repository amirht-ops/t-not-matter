namespace AuthorizationService.Api.Contracts.Requests;

public sealed record AssignRoleRequest(Guid SubjectId, Guid RoleId, Guid AssignedBy);
