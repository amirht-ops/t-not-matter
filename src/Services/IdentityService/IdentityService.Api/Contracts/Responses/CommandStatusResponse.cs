namespace IdentityService.Api.Contracts.Responses;

public sealed record CommandStatusResponse(
    bool LoggedOut = false,
    bool Verified = false,
    bool Enabled = false,
    bool Disabled = false,
    bool Activated = false,
    bool Unlocked = false,
    bool Deleted = false);
