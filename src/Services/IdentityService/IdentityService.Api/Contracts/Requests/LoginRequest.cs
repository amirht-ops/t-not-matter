namespace IdentityService.Api.Contracts.Requests;

public sealed record LoginRequest(string? Identifier, string? UserName, string Password, string? MfaCode);