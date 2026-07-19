namespace IdentityService.Api.Contracts.Requests;

public sealed record MfaVerifyRequest(Guid UserId, string Code);
