namespace IdentityService.Application.Features.GetUser;

public sealed record GetUserResponse(Guid UserId, string Username, string PhoneNumber, string Status, Guid TenantId);
