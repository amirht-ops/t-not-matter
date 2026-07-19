namespace AuthorizationService.Application.Features.GetEffectivePermissions;

public sealed record GetEffectivePermissionsResponse(Guid SubjectId, IReadOnlyCollection<string> Permissions);
