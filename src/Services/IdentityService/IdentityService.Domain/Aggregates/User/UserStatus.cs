namespace IdentityService.Domain.Aggregates.User;

public enum UserStatus
{
    Pending = 1,
    Active = 2,
    Locked = 3,
    Disabled = 4,
    Deleted = 5
}
