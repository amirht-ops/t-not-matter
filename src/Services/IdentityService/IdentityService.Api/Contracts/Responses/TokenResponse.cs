namespace IdentityService.Api.Contracts.Responses;

public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, Guid SessionId, bool MfaRequired);
