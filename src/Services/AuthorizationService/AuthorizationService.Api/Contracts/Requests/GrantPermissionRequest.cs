namespace AuthorizationService.Api.Contracts.Requests;

public sealed record GrantPermissionRequest(Guid RoleId, Guid PermissionId, Guid GrantedBy);
