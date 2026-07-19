namespace AuthorizationService.Api.Contracts.Requests;

public sealed record ChangeUserRoleRequest(Guid SubjectId, Guid NewRoleId, Guid AssignedBy);
