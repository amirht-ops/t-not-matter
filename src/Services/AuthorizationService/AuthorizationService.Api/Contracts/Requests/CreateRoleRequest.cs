namespace AuthorizationService.Api.Contracts.Requests;

public sealed record CreateRoleRequest(string Name, string? Description, Guid? ParentRoleId, Guid DepartmentId);
