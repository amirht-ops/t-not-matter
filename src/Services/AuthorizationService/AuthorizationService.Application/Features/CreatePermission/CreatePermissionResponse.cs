namespace AuthorizationService.Application.Features.CreatePermission;

public sealed record CreatePermissionResponse(Guid PermissionId, string Key, string Action, string ResourceType);
