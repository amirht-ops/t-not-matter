namespace AuthorizationService.Api.Contracts.Requests;

public sealed record RevokeRoleRequest(Guid SubjectId, Guid RoleId);
