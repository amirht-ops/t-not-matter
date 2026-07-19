using SharedKernel.Domain.Events;

namespace IdentityService.Domain.Events;

public abstract record IdentityDomainEvents(Guid TenantId, Guid CorrelationId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    public int Version { get; } = 1;
    public Guid? CausationId { get; init; }
    public abstract string EventTypeName { get; }
}

public sealed record UserRegisteredDomainEvent(
    Guid UserId,
    Guid TenantIdValue,
    string UsernameValue,
    string PhoneNumberValue,
    string? EmailValue,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserRegisteredV1";
}

public sealed record UserLoggedInDomainEvent(Guid UserId, Guid TenantIdValue, Guid SessionId, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserLoggedInV1";
}

public sealed record UserLockedDomainEvent(
    Guid UserId,
    Guid TenantIdValue,
    int FailedLoginAttempts,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserLockedV1";
}

public sealed record MfaEnabledDomainEvent(
    Guid UserId,
    Guid TenantIdValue,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "MfaEnabledV1";
}

public sealed record MfaDisabledDomainEvent(
    Guid UserId,
    Guid TenantIdValue,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "MfaDisabledV1";
}

public sealed record PasswordChangedDomainEvent(
    Guid UserId,
    Guid TenantIdValue,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "PasswordChangedV1";
}

public sealed record PasswordResetDomainEvent(
    Guid UserId,
    Guid TenantIdValue,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "PasswordResetV1";
}

public sealed record SessionRevokedDomainEvent(
    Guid SessionId,
    Guid UserId,
    Guid TenantIdValue,
    Guid CorrelationIdValue) : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "SessionRevokedV1";
}

public sealed record UserActivatedDomainEvent(Guid UserId, Guid TenantIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserActivatedV1";
}

public sealed record UserSynchronizedDomainEvent(Guid UserId, Guid TenantIdValue, Guid DepartmentIdValue, Guid RoleIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserSynchronizedV1";
}

public sealed record UserUnlockedDomainEvent(Guid UserId, Guid TenantIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserUnlockedV1";
}

public sealed record UserDisabledDomainEvent(Guid UserId, Guid TenantIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserDisabledV1";
}

public sealed record UserDeletedDomainEvent(Guid UserId, Guid TenantIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "UserDeletedV1";
}

public sealed record SessionCreatedDomainEvent(Guid SessionId, Guid UserId, Guid TenantIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "SessionCreatedV1";
}

public sealed record SessionRefreshTokenRotatedDomainEvent(Guid SessionId, Guid UserId, Guid TenantIdValue, Guid CorrelationIdValue)
    : IdentityDomainEvents(TenantIdValue, CorrelationIdValue)
{
    public override string EventTypeName => "SessionRefreshTokenRotatedV1";
}

