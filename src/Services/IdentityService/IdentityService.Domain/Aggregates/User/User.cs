using IdentityService.Domain.Errors;
using IdentityService.Domain.Events;
using IdentityService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace IdentityService.Domain.Aggregates.User;

public sealed class User : AggregateRoot
{
    private User(
        Guid id,
        Guid tenantId,
        Username username,
        PhoneNumber phoneNumber,
        PasswordHash passwordHash,
        Email? email) : base(id, tenantId)
    {
        Username = username;
        PhoneNumber = phoneNumber;
        Email = email;
        PasswordHash = passwordHash;

        Status = UserStatus.Pending;
    }

    public Email? Email { get; private set; }
    public Username Username { get; private set; }
    public PasswordHash PasswordHash { get; private set; }
    public PhoneNumber PhoneNumber { get; private set; }
    public UserStatus Status { get; private set; }
    public MfaSettings MfaSettings { get; private set; } = MfaSettings.Disabled;
    public bool CanAuthenticate => Status == UserStatus.Active;

    public static User Register(Guid tenantId, Username username, PhoneNumber phoneNumber,
        PasswordHash passwordHash,
        Email? email, Guid correlationId)
    {
        var user = new User(Guid.NewGuid(), tenantId, username, phoneNumber, passwordHash, email);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, tenantId, username.Value,
            phoneNumber.Value, email?.Value, correlationId));
        return user;
    }

    /// <summary>
    /// Reconstructs a User from persisted state. Used by the caching layer to materialize
    /// a User from a cached DTO without replaying state transitions or raising domain events.
    /// This is NOT a business operation — it's a persistence helper.
    /// </summary>
    public static User Rehydrate(
        Guid id,
        Guid tenantId,
        Username username,
        PhoneNumber phoneNumber,
        PasswordHash passwordHash,
        Email? email,
        UserStatus status,
        MfaSettings mfaSettings,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        int version)
    {
        var user = new User(id, tenantId, username, phoneNumber, passwordHash, email)
        {
            Status = status,
            MfaSettings = mfaSettings
        };
        user.SetTimestamps(createdAt, updatedAt, version);
        return user;
    }

    public Result<Unit> Activate(Guid correlationId)
    {
        var transition = EnsureTransition(UserStatus.Pending, UserStatus.Active);
        if (transition.IsFailure)
        {
            return Result<Unit>.Failure(transition.Error);
        }

        Status = UserStatus.Active;
        MarkUpdated();
        RaiseDomainEvent(new UserActivatedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> SynchronizeTenant(Guid tenantId, Guid departmentId, Guid roleId, Guid correlationId)
    {
        if (TenantId != tenantId)
            MoveToTenant(tenantId);
        else
            MarkUpdated();

        RaiseDomainEvent(new UserSynchronizedDomainEvent(Id, tenantId, departmentId, roleId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Unlock(Guid correlationId)
    {
        var transition = EnsureTransition(UserStatus.Locked, UserStatus.Active);
        if (transition.IsFailure)
        {
            return Result<Unit>.Failure(transition.Error);
        }

        Status = UserStatus.Active;
        MarkUpdated();
        RaiseDomainEvent(new UserUnlockedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Disable(Guid correlationId)
    {
        if (Status is not (UserStatus.Active or UserStatus.Locked))
        {
            return Result<Unit>.Failure(IdentityErrors.InvalidStatusTransition);
        }

        Status = UserStatus.Disabled;
        MarkUpdated();
        RaiseDomainEvent(new UserDisabledDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Delete(Guid correlationId)
    {
        var transition = EnsureTransition(UserStatus.Disabled, UserStatus.Deleted);
        if (transition.IsFailure)
        {
            return Result<Unit>.Failure(transition.Error);
        }

        Status = UserStatus.Deleted;
        SoftDelete();
        RaiseDomainEvent(new UserDeletedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> ChallengeMfa(Guid correlationId)
    {
        var authCheck = EnsureCanAuthenticate();
        if (authCheck.IsFailure)
        {
            return Result<Unit>.Failure(authCheck.Error);
        }

        MarkUpdated();
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> VerifyMfa(Guid correlationId)
    {
        var authCheck = EnsureCanAuthenticate();
        if (authCheck.IsFailure)
        {
            return Result<Unit>.Failure(authCheck.Error);
        }

        MarkUpdated();
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> EnableMfa(string protectedSecret, Guid correlationId)
    {
        var authCheck = EnsureCanAuthenticate();
        if (authCheck.IsFailure)
        {
            return Result<Unit>.Failure(authCheck.Error);
        }

        if (MfaSettings.IsEnabled)
        {
            return Result<Unit>.Failure(IdentityErrors.MfaAlreadyEnabled);
        }

        var mfaResult = MfaSettings.Enable(protectedSecret);
        if (mfaResult.IsFailure)
        {
            return Result<Unit>.Failure(mfaResult.Error);
        }

        MfaSettings = mfaResult.Value!;
        RaiseDomainEvent(new MfaEnabledDomainEvent(Id, TenantId, correlationId));
        MarkUpdated();
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> DisableMfa(Guid correlationId)
    {
        var authCheck = EnsureCanAuthenticate();
        if (authCheck.IsFailure)
        {
            return Result<Unit>.Failure(authCheck.Error);
        }

        if (!MfaSettings.IsEnabled)
        {
            return Result<Unit>.Failure(IdentityErrors.MfaNotEnabled);
        }

        MfaSettings = MfaSettings.Disabled;
        RaiseDomainEvent(new MfaDisabledDomainEvent(Id, TenantId, correlationId));
        MarkUpdated();
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> ChangePassword(PasswordHash currentPasswordHash, PasswordHash newPasswordHash,
        Guid correlationId)
    {
        var authCheck = EnsureCanAuthenticate();
        if (authCheck.IsFailure)
        {
            return Result<Unit>.Failure(authCheck.Error);
        }

        if (currentPasswordHash.Value == newPasswordHash.Value)
        {
            return Result<Unit>.Failure(IdentityErrors.NewPasswordSameAsCurrent);
        }

        PasswordHash = newPasswordHash;
        RaiseDomainEvent(new PasswordChangedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> ResetPassword(PasswordHash newPasswordHash, Guid correlationId)
    {
        var authCheck = EnsureCanAuthenticate();
        if (authCheck.IsFailure)
        {
            return Result<Unit>.Failure(authCheck.Error);
        }

        PasswordHash = newPasswordHash;
        RaiseDomainEvent(new PasswordResetDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Lockout(Guid correlationId)
    {
        if (Status == UserStatus.Locked)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        if (Status != UserStatus.Active)
        {
            return Result<Unit>.Failure(IdentityErrors.UserMustBeActive);
        }

        Status = UserStatus.Locked;
        MarkUpdated();
        RaiseDomainEvent(new UserLockedDomainEvent(Id, TenantId, 0, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    private Result<Unit> EnsureCanAuthenticate()
    {
        if (!CanAuthenticate)
        {
            if (Status == UserStatus.Locked)
            {
                return Result<Unit>.Failure(IdentityErrors.UserLocked);
            }

            return Result<Unit>.Failure(IdentityErrors.UserMustBeActive);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private Result<Unit> EnsureTransition(UserStatus expectedCurrent, UserStatus target)
    {
        if (Status != expectedCurrent)
        {
            return Result<Unit>.Failure(IdentityErrors.InvalidStatusTransition);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
