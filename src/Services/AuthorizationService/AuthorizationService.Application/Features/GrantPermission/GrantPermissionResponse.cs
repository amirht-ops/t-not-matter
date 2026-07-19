namespace AuthorizationService.Application.Features.GrantPermission;

public sealed record GrantPermissionResponse(Guid GrantId, Guid RoleId, Guid PermissionId, DateTimeOffset GrantedAtUtc);
