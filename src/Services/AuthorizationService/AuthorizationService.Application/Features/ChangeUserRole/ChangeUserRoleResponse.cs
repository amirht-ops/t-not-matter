namespace AuthorizationService.Application.Features.ChangeUserRole;

public sealed record ChangeUserRoleResponse(Guid OldAssignmentId, Guid NewAssignmentId, Guid SubjectId, Guid PreviousRoleId, Guid NewRoleId, DateTimeOffset AssignedAtUtc);
