namespace IdentityService.Infrastructure.Caching;

internal sealed record UserCacheDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool MfaEnabled { get; init; }
    public string? MfaProtectedSecret { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Version { get; init; }
    public bool IsDeleted { get; init; }
}
