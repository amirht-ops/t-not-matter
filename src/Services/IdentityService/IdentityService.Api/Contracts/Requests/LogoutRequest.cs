namespace IdentityService.Api.Contracts.Requests;

public sealed record LogoutRequest(Guid SessionId);
