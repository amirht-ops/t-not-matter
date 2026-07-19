namespace AuthorizationService.Application.Features.AssignRole;

public sealed record AssignRoleResponse(Guid AssignmentId, Guid SubjectId, Guid RoleId, DateTimeOffset AssignedAtUtc);
