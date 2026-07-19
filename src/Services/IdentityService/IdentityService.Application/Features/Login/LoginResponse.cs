namespace IdentityService.Application.Features.Login;

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    Guid SessionId,
    bool MfaRequired);