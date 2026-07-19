using IdentityService.Domain.Aggregates.User;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.ValueObjects;
using SharedKernel.Caching;

namespace IdentityService.Infrastructure.Caching;

public sealed class CachedUserRepository(IUserRepository inner, IDistributedCacheService cache) : IUserRepository
{
    private static readonly CacheEntryOptions PositiveCacheOptions = new() { SlidingExpiration = TimeSpan.FromSeconds(30) };
    private static readonly CacheEntryOptions NegativeCacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3) };

    public async Task<User?> GetByIdAsync(Guid tenantId, Guid userId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByIdAsync(tenantId, userId, trackChanges, cancellationToken);

        var key = UserByIdKey(tenantId, userId);
        var cached = await cache.GetAsync<UserCacheDto>(key, cancellationToken);
        if (cached is not null)
        {
            return ToUser(cached);
        }

        var user = await inner.GetByIdAsync(tenantId, userId, trackChanges, cancellationToken);
        if (user is not null)
            await cache.SetAsync(key, ToDto(user), PositiveCacheOptions, cancellationToken);
        else
            await cache.SetAsync(key, (UserCacheDto?)null!, NegativeCacheOptions, cancellationToken);
        return user;
    }

    public Task<User?> GetByIdAcrossTenantsAsync(Guid userId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        return inner.GetByIdAcrossTenantsAsync(userId, trackChanges, cancellationToken);
    }

    public async Task<User?> GetByUsernameAsync(Username username, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByUsernameAsync(username, tenantId, trackChanges, cancellationToken);

        var key = UserByUsernameKey(tenantId, username);
        var cached = await cache.GetAsync<UserCacheDto>(key, cancellationToken);
        if (cached is not null)
        {
            return ToUser(cached);
        }

        var user = await inner.GetByUsernameAsync(username, tenantId, trackChanges, cancellationToken);
        if (user is not null)
            await cache.SetAsync(key, ToDto(user), PositiveCacheOptions, cancellationToken);
        else
            await cache.SetAsync(key, (UserCacheDto?)null!, NegativeCacheOptions, cancellationToken);
        return user;
    }

    public async Task<User?> GetByUsernameUnSafeAsync(Username username, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByUsernameUnSafeAsync(username, tenantId, trackChanges, cancellationToken);

        var key = UserByUsernameKey(tenantId, username);
        var cached = await cache.GetAsync<UserCacheDto>(key, cancellationToken);
        if (cached is not null)
        {
            return ToUser(cached);
        }

        var user = await inner.GetByUsernameUnSafeAsync(username, tenantId, trackChanges, cancellationToken);
        if (user is not null)
            await cache.SetAsync(key, ToDto(user), PositiveCacheOptions, cancellationToken);
        else
            await cache.SetAsync(key, (UserCacheDto?)null!, NegativeCacheOptions, cancellationToken);
        return user;
    }

    public async Task<bool> ExistsByUsernameAsync(Username username, Guid tenantId, CancellationToken cancellationToken)
    {
        var key = UserExistsByUsernameKey(tenantId, username);
        var cached = await cache.GetAsync<bool?>(key, cancellationToken);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var exists = await inner.ExistsByUsernameAsync(username, tenantId, cancellationToken);
        await cache.SetAsync(key, exists, exists ? PositiveCacheOptions : NegativeCacheOptions, cancellationToken);
        return exists;
    }

    public async Task<User?> GetByEmailAsync(Email email, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByEmailAsync(email, tenantId, trackChanges, cancellationToken);

        var key = UserByEmailKey(tenantId, email);
        var cached = await cache.GetAsync<UserCacheDto>(key, cancellationToken);
        if (cached is not null)
        {
            return ToUser(cached);
        }

        var user = await inner.GetByEmailAsync(email, tenantId, trackChanges, cancellationToken);
        if (user is not null)
            await cache.SetAsync(key, ToDto(user), PositiveCacheOptions, cancellationToken);
        else
            await cache.SetAsync(key, (UserCacheDto?)null!, NegativeCacheOptions, cancellationToken);
        return user;
    }

    public async Task<bool> ExistsByEmailAsync(Email email, Guid tenantId, CancellationToken cancellationToken)
    {
        var key = UserExistsByEmailKey(tenantId, email);
        var cached = await cache.GetAsync<bool?>(key, cancellationToken);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var exists = await inner.ExistsByEmailAsync(email, tenantId, cancellationToken);
        await cache.SetAsync(key, exists, exists ? PositiveCacheOptions : NegativeCacheOptions, cancellationToken);
        return exists;
    }

    public async Task<User?> GetByPhoneNumberAsync(PhoneNumber phoneNumber, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        if (trackChanges)
            return await inner.GetByPhoneNumberAsync(phoneNumber, tenantId, trackChanges, cancellationToken);

        var key = UserByPhoneNumberKey(tenantId, phoneNumber);
        var cached = await cache.GetAsync<UserCacheDto>(key, cancellationToken);
        if (cached is not null)
        {
            return ToUser(cached);
        }

        var user = await inner.GetByPhoneNumberAsync(phoneNumber, tenantId, trackChanges, cancellationToken);
        if (user is not null)
            await cache.SetAsync(key, ToDto(user), PositiveCacheOptions, cancellationToken);
        else
            await cache.SetAsync(key, (UserCacheDto?)null!, NegativeCacheOptions, cancellationToken);
        return user;
    }

    public async Task<bool> ExistsByPhoneNumberAsync(PhoneNumber phoneNumber, Guid tenantId, CancellationToken cancellationToken)
    {
        var key = UserExistsByPhoneNumberKey(tenantId, phoneNumber);
        var cached = await cache.GetAsync<bool?>(key, cancellationToken);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var exists = await inner.ExistsByPhoneNumberAsync(phoneNumber, tenantId, cancellationToken);
        await cache.SetAsync(key, exists, exists ? PositiveCacheOptions : NegativeCacheOptions, cancellationToken);
        return exists;
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        await inner.AddAsync(user, cancellationToken);
        var invalidations = new List<Task>
        {
            cache.RemoveAsync(UserByUsernameKey(user.TenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserExistsByUsernameKey(user.TenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserByPhoneNumberKey(user.TenantId, user.PhoneNumber), cancellationToken),
            cache.RemoveAsync(UserExistsByPhoneNumberKey(user.TenantId, user.PhoneNumber), cancellationToken),
        };
        if (user.Email is not null)
        {
            invalidations.Add(cache.RemoveAsync(UserByEmailKey(user.TenantId, user.Email), cancellationToken));
            invalidations.Add(cache.RemoveAsync(UserExistsByEmailKey(user.TenantId, user.Email), cancellationToken));
        }
        await Task.WhenAll(invalidations);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        await inner.UpdateAsync(user, cancellationToken);
        var invalidations = new List<Task>
        {
            cache.RemoveAsync(UserByIdKey(user.TenantId, user.Id), cancellationToken),
            cache.RemoveAsync(UserByUsernameKey(user.TenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserExistsByUsernameKey(user.TenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserByPhoneNumberKey(user.TenantId, user.PhoneNumber), cancellationToken),
            cache.RemoveAsync(UserExistsByPhoneNumberKey(user.TenantId, user.PhoneNumber), cancellationToken),
        };
        if (user.Email is not null)
        {
            invalidations.Add(cache.RemoveAsync(UserByEmailKey(user.TenantId, user.Email), cancellationToken));
            invalidations.Add(cache.RemoveAsync(UserExistsByEmailKey(user.TenantId, user.Email), cancellationToken));
        }
        await Task.WhenAll(invalidations);
    }

    public async Task MoveToTenantAsync(User user, Guid oldTenantId, CancellationToken cancellationToken = default)
    {
        await inner.MoveToTenantAsync(user, oldTenantId, cancellationToken);
        await Task.WhenAll(
            cache.RemoveAsync(UserByIdKey(oldTenantId, user.Id), cancellationToken),
            cache.RemoveAsync(UserByUsernameKey(oldTenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserExistsByUsernameKey(oldTenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserByIdKey(user.TenantId, user.Id), cancellationToken),
            cache.RemoveAsync(UserByUsernameKey(user.TenantId, user.Username), cancellationToken),
            cache.RemoveAsync(UserExistsByUsernameKey(user.TenantId, user.Username), cancellationToken));
    }

    private static UserCacheDto ToDto(User user) => new()
    {
        Id = user.Id,
        TenantId = user.TenantId,
        Username = user.Username.Value,
        PhoneNumber = user.PhoneNumber.Value,
        PasswordHash = user.PasswordHash.Value,
        Email = user.Email?.Value,
        Status = user.Status.ToString(),
        MfaEnabled = user.MfaSettings.IsEnabled,
        MfaProtectedSecret = user.MfaSettings.ProtectedSecret,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
        Version = user.Version,
        IsDeleted = user.IsDeleted
    };

    /// <summary>
    /// Reconstructs a User from cached DTO. Returns null if reconstruction fails,
    /// triggering a cache miss and fresh database load.
    /// 
    /// Uses User.Rehydrate() to directly set persisted state without replaying
    /// domain operations. FailedLoginAttempts is not reconstructed — it lives
    /// in the AccessRisk aggregate (ADR-008).
    /// </summary>
    private static User? ToUser(UserCacheDto dto)
    {
        if (dto.IsDeleted) return null;

        var username = Username.Create(dto.Username);
        if (username.IsFailure) return null;

        var phoneNumber = PhoneNumber.Create(dto.PhoneNumber);
        if (phoneNumber.IsFailure) return null;

        var passwordHash = PasswordHash.FromHash(dto.PasswordHash);
        if (passwordHash.IsFailure) return null;

        Email? email = null;
        if (!string.IsNullOrEmpty(dto.Email))
        {
            var emailResult = Email.Create(dto.Email);
            if (emailResult.IsSuccess) email = emailResult.Value;
        }

        if (!Enum.TryParse<UserStatus>(dto.Status, out var status))
            status = UserStatus.Pending;

        var mfaSettings = dto.MfaEnabled && dto.MfaProtectedSecret is not null
            ? new MfaSettings(true, dto.MfaProtectedSecret)
            : MfaSettings.Disabled;

        var user = User.Rehydrate(
            dto.Id,
            dto.TenantId,
            username.Value!,
            phoneNumber.Value!,
            passwordHash.Value!,
            email,
            status,
            mfaSettings,
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.Version);

        user.ClearDomainEvents();
        return user;
    }

    private static string UserByIdKey(Guid tenantId, Guid userId) => $"identity:user:id:{tenantId:N}:{userId:N}";
    private static string UserByUsernameKey(Guid tenantId, Username username) => $"identity:user:username:{tenantId:N}:{username.Value}";
    private static string UserExistsByUsernameKey(Guid tenantId, Username username) => $"identity:user:exists-username:{tenantId:N}:{username.Value}";
    private static string UserByEmailKey(Guid tenantId, Email email) => $"identity:user:email:{tenantId:N}:{email.Value}";
    private static string UserExistsByEmailKey(Guid tenantId, Email email) => $"identity:user:exists-email:{tenantId:N}:{email.Value}";
    private static string UserByPhoneNumberKey(Guid tenantId, PhoneNumber phoneNumber) => $"identity:user:phone:{tenantId:N}:{phoneNumber.Value}";
    private static string UserExistsByPhoneNumberKey(Guid tenantId, PhoneNumber phoneNumber) => $"identity:user:exists-phone:{tenantId:N}:{phoneNumber.Value}";
}
