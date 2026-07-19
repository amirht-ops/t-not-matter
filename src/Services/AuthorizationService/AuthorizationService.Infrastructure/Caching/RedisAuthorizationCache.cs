using System.Text.Json;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Models;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AuthorizationService.Infrastructure.Caching;

public sealed class RedisAuthorizationCache(
    IDistributedCache cache,
    IOptions<AuthorizationCacheOptions> options,
    ILogger<RedisAuthorizationCache> logger,
    IServiceProvider serviceProvider) : IAuthorizationCache
{
    public async Task<IReadOnlyCollection<string>?> GetEffectivePermissionsAsync(Guid tenantId, SubjectId subjectId, CancellationToken cancellationToken)
    {
        try
        {
            var key = CacheKeyBuilder.EffectivePermissions(tenantId, subjectId);
            var data = await cache.GetAsync(key, cancellationToken);
            if (data is null)
            {
                logger.LogInformation("Authorization cache miss tenant={TenantId} subject={SubjectId}", tenantId, subjectId.Value);
                return null;
            }
            logger.LogInformation("Authorization cache hit tenant={TenantId} subject={SubjectId}", tenantId, subjectId.Value);
            return JsonSerializer.Deserialize<IReadOnlyCollection<string>>(data);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Authorization cache read failed tenant={TenantId} subject={SubjectId}", tenantId, subjectId.Value);
            return null;
        }
    }

    public async Task SetEffectivePermissionsAsync(Guid tenantId, SubjectId subjectId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        try
        {
            var key = CacheKeyBuilder.EffectivePermissions(tenantId, subjectId);
            var data = JsonSerializer.SerializeToUtf8Bytes(permissions);
            await cache.SetAsync(key, data, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.EffectivePermissionsTtlSeconds)
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Authorization cache write failed tenant={TenantId} subject={SubjectId}", tenantId, subjectId.Value);
        }
    }

    public async Task<AuthorizationDecisionResult?> GetDecisionAsync(Guid tenantId, string decisionHash, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !options.Value.DecisionCacheEnabled) return null;
        try
        {
            var key = CacheKeyBuilder.Decision(tenantId, decisionHash);
            var data = await cache.GetAsync(key, cancellationToken);
            return data is null ? null : JsonSerializer.Deserialize<AuthorizationDecisionResult>(data);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Authorization cache decision read failed tenant={TenantId} hash={Hash}", tenantId, decisionHash);
            return null;
        }
    }

    public async Task SetDecisionAsync(Guid tenantId, string decisionHash, AuthorizationDecisionResult decision, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !options.Value.DecisionCacheEnabled || !decision.IsAllowed) return;
        try
        {
            var key = CacheKeyBuilder.Decision(tenantId, decisionHash);
            var data = JsonSerializer.SerializeToUtf8Bytes(decision);
            await cache.SetAsync(key, data, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.DecisionCacheTtlSeconds)
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Authorization cache decision write failed tenant={TenantId} hash={Hash}", tenantId, decisionHash);
        }
    }

    public async Task InvalidateSubjectAsync(Guid tenantId, SubjectId subjectId, CancellationToken cancellationToken)
    {
        try
        {
            var key = CacheKeyBuilder.EffectivePermissions(tenantId, subjectId);
            await cache.RemoveAsync(key, cancellationToken);
            logger.LogInformation("Authorization cache invalidated for tenant={TenantId} subject={SubjectId}", tenantId, subjectId.Value);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Authorization cache invalidation failed tenant={TenantId} subject={SubjectId}", tenantId, subjectId.Value);
        }
    }

    public async Task InvalidateTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var muxer = serviceProvider.GetService(typeof(IConnectionMultiplexer)) as IConnectionMultiplexer;
            if (muxer is not null)
            {
                var db = muxer.GetDatabase();
                var script = @"
                    local cursor = '0'
                    repeat
                        local result = redis.call('SCAN', cursor, 'MATCH', KEYS[1], 'COUNT', 100)
                        cursor = result[1]
                        local keys = result[2]
                        if #keys > 0 then
                            redis.call('DEL', unpack(keys))
                        end
                    until cursor == '0'";
                await db.ScriptEvaluateAsync(script, new RedisKey[] { $"authz:{tenantId}:*" });
                logger.LogInformation("Authorization cache invalidated for tenant={TenantId} via Redis SCAN/DEL", tenantId);
                return;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis-specific tenant invalidation failed for tenant={TenantId}", tenantId);
        }

        logger.LogWarning("Tenant authorization cache invalidation requested tenant={TenantId} — requires Redis IConnectionMultiplexer for pattern deletion", tenantId);
        await Task.CompletedTask;
    }
}
