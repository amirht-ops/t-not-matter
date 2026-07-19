using System.Security.Cryptography;
using IdentityService.Domain.Events;
using SharedKernel.Domain.Primitives;

namespace IdentityService.Domain.Aggregates.Session;

public sealed class Session : AggregateRoot
{
    private Session() { }

    private Session(Guid id, Guid tenantId, Guid userId, string refreshTokenHash, DateTimeOffset expiresAt, string? ipAddress, string? userAgent)
        : base(id, tenantId)
    {
        UserId = userId;
        RefreshTokenHash = refreshTokenHash;
        ExpiresAt = expiresAt;
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }

    public Guid UserId { get; private set; }
    public string RefreshTokenHash { get; private set; }
    public string? PreviousRefreshTokenHash { get; private set; }
    public DateTimeOffset? RefreshTokenRotatedAt { get; private set; }
    public DateTimeOffset? RefreshTokenReuseDetectedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public bool IsActive => !IsDeleted && RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public static (Session Session, string RefreshToken) Create(Guid tenantId, Guid userId, TimeSpan lifetime, string? ipAddress, string? userAgent, Guid correlationId = default)
    {
        var id = Guid.NewGuid();
        var (refreshToken, plain) = RefreshToken.Create(tenantId, id, DateTimeOffset.UtcNow.Add(lifetime));
        var session = new Session(id, tenantId, userId, refreshToken.TokenHash, refreshToken.ExpiresAt, ipAddress, userAgent);
        session.RaiseDomainEvent(new SessionCreatedDomainEvent(session.Id, userId, tenantId, correlationId));
        return (session, plain);
    }

    public bool MatchesRefreshToken(string refreshToken)
    {
        if (!RefreshToken.TryParse(refreshToken, out _, out _, out var randomPart))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(RefreshToken.Hash(randomPart)),
            Convert.FromHexString(RefreshTokenHash));
    }

    public string RotateRefreshToken(TimeSpan lifetime, Guid correlationId = default)
    {
        var (refreshToken, plain) = RefreshToken.Create(TenantId, Id, DateTimeOffset.UtcNow.Add(lifetime));
        PreviousRefreshTokenHash = RefreshTokenHash;
        RefreshTokenHash = refreshToken.TokenHash;
        ExpiresAt = refreshToken.ExpiresAt;
        RefreshTokenRotatedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        RaiseDomainEvent(new SessionRefreshTokenRotatedDomainEvent(Id, UserId, TenantId, correlationId));
        return plain;
    }

    public bool MatchesPreviousRefreshToken(string refreshToken)
    {
        if (PreviousRefreshTokenHash is null)
            return false;

        if (!RefreshToken.TryParse(refreshToken, out _, out _, out var randomPart))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(RefreshToken.Hash(randomPart)),
            Convert.FromHexString(PreviousRefreshTokenHash));
    }

    public void MarkRefreshTokenReuseDetected(Guid correlationId)
    {
        RefreshTokenReuseDetectedAt ??= DateTimeOffset.UtcNow;
        Revoke(correlationId);
    }

    public void Revoke(Guid correlationId)
    {
        if (RevokedAt is not null) return;
        RevokedAt = DateTimeOffset.UtcNow;
        SoftDelete();
        RaiseDomainEvent(new SessionRevokedDomainEvent(Id, UserId, TenantId, correlationId));
    }
}
