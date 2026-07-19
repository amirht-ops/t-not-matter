namespace IdentityService.Api.Contracts.Requests;

public sealed record RegisterRequest(
    string UserName,
    string PhoneNumber,
    string Password,
    string Email);
