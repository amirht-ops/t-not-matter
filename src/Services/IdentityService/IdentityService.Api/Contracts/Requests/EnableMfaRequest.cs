namespace IdentityService.Api.Contracts.Requests;

public sealed record EnableMfaRequest(Guid UserId, string Secret);
