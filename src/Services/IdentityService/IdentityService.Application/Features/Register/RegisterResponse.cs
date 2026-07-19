namespace IdentityService.Application.Features.Register;

public sealed record RegisterResponse(Guid UserId, string Username, string Status);
