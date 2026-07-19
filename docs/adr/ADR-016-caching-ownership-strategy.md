# ADR-016

Title:

Platform Caching Ownership Strategy — Categories, Boundaries, and Architectural Rules

Status:

Proposed

---

# 1. Context

The platform is composed of multiple services — IdentityService, TenantService, AuthorizationService — all sharing a common Redis infrastructure via Platform.Caching. A comprehensive codebase audit (`docs/adr/ADR-016-caching-ownership-strategy.md#appendix-a-cache-inventory`) reveals 91 files touching caching concerns across SharedKernel, Platform, and every service.

Recent ADRs established clear ownership boundaries:

- **ADR-013:** IdentityService owns Tenant Resolution. Platform owns Tenant Propagation only. Runtime components must never perform business lookups.
- **ADR-012:** Platform owns RequestContext propagation. Execution context is infrastructure state, not business data.
- **ADR-014:** AuthorizationService owns Department Membership (via Role). IdentityService does not own Department Membership.
- **ADR-011:** AuthorizationService owns Role, Permission, RoleAssignment. Authorization decisions must never call TenantService synchronously.

Caching has not been subjected to the same ownership model. Today, caches of every category — business, infrastructure, computation — coexist in the same Redis instance, use ad-hoc key formats, have inconsistent TTLs, and lack a unified invalidation contract. Platform.Caching provides generic Redis primitives but does not enforce what may be cached where.

---

# 2. Problem Statement

Without a caching ownership model, the platform exhibits several risks:

### 2.1 No Clear Cache Ownership

Business caches (tenant metadata, user aggregates, permission decisions) and infrastructure caches (rate limiting, distributed locks, idempotency) share the same Redis database. There is no rule preventing Platform from introducing a business cache, or a service from introducing an infrastructure cache that duplicates Platform functionality.

### 2.2 Platform Could Cache Business Data Today

Platform.Caching's `RedisCacheService` is generic — it will cache any serializable type. `CachingBehavior` (Platform.Behaviors) auto-caches any `ICachedQuery` response, which includes business data like permission sets and user profiles. There is no governance over what `ICachedQuery` caches may be defined.

### 2.3 Inconsistent Key Naming

Three independent `CacheKeyBuilder` classes exist (SharedKernel, AuthorizationService, plus inline keys in `CachedTenantRepository`, `CachedUserRepository`, `SessionCache`). Key formats are inconsistent: some use snake_case, some use kebab-case, some use `N` format Guids, some use raw strings. No single canonical convention exists.

### 2.4 No Unified Invalidation Strategy

`InvalidateByPatternAsync` exists in `IDistributedCacheService` but is never called (ADR-009 §4). Event-driven invalidation is partially implemented (`TenantCacheInvalidationConsumer`, `AuthorizationCacheInvalidationConsumer`) but `AuthorizationCacheInvalidationConsumer` is never wired to MassTransit (ADR-009 §Phase 2). Several ICachedQuery implementations are dead code (lack `CachingBehavior` registration). Without a standard, every new cache requires bespoke invalidation logic.

### 2.5 No Failure Strategy Documentation

Every Redis operation is fail-safe (defaults to cache miss / allow), but this is an implementation detail, not an architectural contract. There is no documented statement that "the platform must remain correct if every cache is disabled."

### 2.6 Mixed Negative and Positive Caching

`CachedUserRepository` uses 3s negative caching for "not found" entries alongside 30s positive caching for found entries. This is undocumented and risks stale "not found" results when entities are created.

---

# 3. Design Goals

1. **Clear ownership** — Every cache has exactly one owner (service or platform). No ambiguity.
2. **No business data in Platform caches** — Platform caches infrastructure state only. Business caches belong to services.
3. **Canonical key naming** — Every cache key follows a single convention: `<context>:<entity>:<identifier>[:v<version>]`.
4. **Correctness without cache** — The platform must remain functionally correct if every cache is disabled.
5. **Fail-safe by contract** — Cache failures must never cause the platform to reject valid requests or accept invalid ones.
6. **Explicit invalidation** — Every cache must have a documented invalidation strategy (event-driven, TTL, or manual).
7. **Evictability** — No cache entry contains the sole copy of any data. The database is always the source of truth.

---

# 4. Cache Categories

Every cache in the platform is classified into exactly one category.

## 4.1 Business Cache

Caches domain knowledge. Represents facts about the business domain.

| Attribute | Rule |
|-----------|------|
| Content | Tenant metadata, user profiles, permission sets, role assignments, department data, session activity |
| Owner | The business service that owns the domain concept |
| TTL | Short (≤5 minutes) to limit staleness |
| Invalidation | Required — event-driven or TTL |
| Location | Service infrastructure (IdentityService.Infrastructure, TenantService.Infrastructure, AuthorizationService.Infrastructure) |
| Platform role | **Never located in Platform projects** |

Examples from the codebase:
- `CachedUserRepository` (IdentityService) — user aggregates
- `CachedTenantRepository` (TenantService) — tenant metadata
- `CachedTenantServiceClient` (IdentityService) — tenant slug map
- `RedisAuthorizationCache` (AuthorizationService) — permission sets and decisions
- `SessionCache` (IdentityService) — session activity
- `CachedSessionRepository` (IdentityService) — session aggregates
- `CachedAuthorizationDecisionService` (IdentityService) — platform admin authz decisions
- `RedisRealTimeUsageStore` (AuthorizationService) — quota tracking
- All `ICachedQuery` implementations returning business data (permissions, users, departments)

## 4.2 Infrastructure Cache

Caches operational state. Represents coordination, synchronization, throttling, deduplication.

| Attribute | Rule |
|-----------|------|
| Owner | Platform |
| Content | Lock ownership, rate limit counters, idempotency keys, event deduplication tokens, startup readiness |
| TTL | Short (seconds to minutes) — no business value in long retention |
| Invalidation | Automatic (TTL expiry) |
| Source | Platform projects only |

**Examples from the codebase**:
- `RedisDistributedLockService` (Platform.Caching) — distributed lock ownership
- `RedisDistributedRateLimiterService` (Platform.Caching) — rate limit window counters
- `DistributedIdempotencyStore` (Platform.Caching) — idempotency tracking
- `RedisEventConsumerDeduplicationGuard` (Platform.Caching) — event deduplication
- `RedisConnectionFactory` (Platform.Caching) — connection state

## 4.3 Computation Cache

Caches the result of an expensive deterministic computation.

| Attribute | Rule |
|-----------|------|
| Owner | Explicitly documented (service or platform) |
| Content | Aggregations, report outputs, computed read models |
| TTL | Based on computation cost vs. staleness tolerance |
| Invalidation | TTL or event-driven |
| Source | Service or platform, documented per case |

**Examples from the codebase**:
- `GetEffectivePermissionsQuery` → `ICachedQuery` returning computed effective permission sets (AuthorizationService)
- `ListDepartmentsQuery` → `ICachedQuery` returning department list (TenantService)
- Monthly quota reset is a scheduled operation, not a computation cache per se

## 4.4 Negative Cache

Caches the absence of a result.

| Attribute | Rule |
|-----------|------|
| Owner | Same as the corresponding positive cache |
| Content | "Not found" sentinels for specific lookups |
| TTL | **Must be short** (≤5 seconds) |
| Invalidation | Must be invalidated when the entity could be created |
| Risk | Stale negative caches cause false "not found" errors — high risk |

**Examples from the codebase**:
- `CachedUserRepository` negative cache: 3s absolute for user/email/username/phone not-found
- `CachedSessionRepository` negative cache: 3s absolute
- `CachedTenantServiceClient` defines `NotFoundSentinel` but readers do not check for it — latent inconsistency

---

# 5. Allowed Cache Locations

| Owner | May cache | May NOT cache |
|-------|-----------|---------------|
| **Platform** (Platform.Caching, Platform.Behaviors, Platform.Middleware, Platform.Infrastructure) | Infrastructure state: distributed locks, rate limiting counters, idempotency keys, event dedup tokens, Redis connection state, startup readiness | Tenant metadata, user profiles, permission decisions, role assignments, department data, session activity, domain entities |
| **IdentityService** | Business: user aggregates, session data, tenant slug map (resolution-time only), platform admin authz decisions; Computation: user query results | Infrastructure caches (locks, rate limits, idempotency — delegate to Platform) |
| **TenantService** | Business: tenant aggregates, tenant metadata, department data; Computation: tenant lookup results | Infrastructure caches (delegate to Platform) |
| **AuthorizationService** | Business: effective permissions, authorization decisions, role data, quota/usage tracking; Computation: permission evaluation results | Infrastructure caches (delegate to Platform) |
| **SharedKernel** | Abstractions only: `CacheKeyBuilder`, `CacheEntryOptions`, `ICacheService`, `ICachedQuery`, `IIdempotentRequest`, `SharedTenantCacheDto` | Concrete cache stores (never reference Redis or `IMemoryCache` directly) |
| **AuthorizationService.Domain.Tests, IdentityService.IntegrationTests** | Test doubles, mock setups | Production cache infrastructure |

---

# 6. Forbidden Cache Usage

These patterns are forbidden by this ADR:

### 6.1 Platform Caching Business Data

Platform projects (`Platform.*`) must not cache tenant metadata, user profiles, permission decisions, role assignments, department data, session state, or any domain entity.

Exception: `ICachedQuery` results may pass through Platform.Behaviors `CachingBehavior` because the query and cache key are defined by the service, not by Platform. Platform is a pass-through, not an owner.

### 6.2 Services Caching Infrastructure State

Services must not implement their own distributed locks, rate limiters, idempotency stores, or deduplication guards. These are provided by Platform.Caching and must be consumed via DI, not reimplemented.

Exception: ASP.NET Core's built-in in-process `AddRateLimiter` (used in IdentityService.Api) is acceptable for API-specific rate limiting that does not need to be distributed.

### 6.3 Caching RequestContext or Execution Context

Per ADR-012 Rule 4, execution context must never be cached. It describes *how* and *by whom* a request is executed; it is never business state and must never be persisted to any cache.

### 6.4 Long-Lived Business Caches

Business caches must not exceed 5 minutes TTL unless explicitly documented and justified. Security-critical caches (permission decisions) must not exceed 30 seconds.

### 6.5 Caching Without Invalidation Strategy

Every business cache must document its invalidation strategy (event-driven, TTL, manual, or combination). A cache with only a TTL and no invalidation is unacceptable for mutable business data.

### 6.6 Implicit Negative Caching

Negative caching must be explicitly designed, not incidental. The sentinel value must be robust to serialization round-trips, and every read path must handle it. TTL for negative caches must not exceed 5 seconds.

### 6.7 Cross-Service TenantId Resolution Caches in Platform

Per ADR-013 §8, Platform must never contain `slug → cache → tenant` lookup. The slug→id cache belongs to IdentityService business flows (CachedTenantServiceClient).

---

# 7. Cache Ownership Model

## 7.1 Ownership Matrix

| Cache Entry | Category | Owner | Invalidated By |
|-------------|----------|-------|----------------|
| `lock:*` | Infrastructure | Platform.Caching | TTL expiry (30s default) |
| `idempotency:*` | Infrastructure | Platform.Caching | TTL expiry (5 min) |
| `ratelimit:*:*` | Infrastructure | Platform.Caching | TTL expiry (window-based) |
| `event-consumer:*:*` | Infrastructure | Platform.Caching | TTL expiry (7d) |
| `identity:user:id:*` | Business | IdentityService | Event-driven (UserActivated, UserDisabled) + TTL (30s) |
| `identity:user:username:*` | Business | IdentityService | Event-driven + TTL (30s) |
| `identity:user:email:*` | Business | IdentityService | Event-driven + TTL (30s) |
| `identity:user:phone:*` | Business | IdentityService | Event-driven + TTL (30s) |
| `identity:session:id:*` | Business | IdentityService | Write-through + TTL (30s) |
| `identity:session:v1:*` (IMemoryCache) | Business | IdentityService | TTL (5 min sliding) |
| `platform:shared:tenant:slug:*` | Business | IdentityService (resolution) / TenantService (write-through) | Event-driven (TenantCreatedV1) + TTL (5 min) |
| `tenant-service:tenant:id:*` | Business | TenantService | Write-through + TTL (30s) |
| `tenant-service:tenant:code:*` | Business | TenantService | Write-through + TTL (30s) |
| `authz:*:*` (RedisAuthorizationCache) | Business | AuthorizationService | Event-driven (role/permission events) + TTL (5s) |
| `quota:*` (RedisRealTimeUsageStore) | Business | AuthorizationService | Scheduled (MonthlyQuotaResetJob) |
| `authz:platform:admin:*` | Business | IdentityService | TTL (10 min) |
| `effective-permissions:*` (ICachedQuery) | Computation | AuthorizationService | TTL (5 min) |
| `user:*` (ICachedQuery) | Computation | IdentityService | TTL (5 min) |
| `tenant-service:by-slug:*` (ICachedQuery) | Computation | TenantService | TTL (5 min) |
| `departments:*` (ICachedQuery) | Computation | TenantService | Event-driven (TenantCacheInvalidationConsumer) + TTL (5 min) |
| `department:*` (ICachedQuery) | Computation | TenantService | Event-driven + TTL (5 min) |
| `identity:user:exists-*` (negative) | Negative | IdentityService | TTL (3s) |
| `identity:user:exists-email:*` (negative) | Negative | IdentityService | TTL (3s) |
| `identity:user:exists-phone:*` (negative) | Negative | IdentityService | TTL (3s) |
| `identity:session:id:*` (negative) | Negative | IdentityService | TTL (3s) |

## 7.2 Service vs. Platform Responsibility

```
                    ┌─────────────────────────────┐
                    │         Platform             │
                    │  ┌───────────────────────┐   │
                    │  │  Platform.Caching     │   │
                    │  │  - RedisCacheService  │   │
                    │  │  - Locks, RateLimit   │   │
                    │  │  - Idempotency/Dedup  │   │
                    │  │  - Redis connection   │   │
                    │  └───────────────────────┘   │
                    │  ┌───────────────────────┐   │
                    │  │  Platform.Behaviors   │   │
                    │  │  - CachingBehavior    │   │
                    │  │  - IdempotencyBehavior│   │
                    │  └───────────────────────┘   │
                    │  ┌───────────────────────┐   │
                    │  │ Platform.Infrastructure│   │
                    │  │ - Redis warmup        │   │
                    │  │ - Readiness monitor   │   │
                    │  └───────────────────────┘   │
                    └──────────┬──────────────────┘
                               │ provides Redis primitives
                               ▼
┌─────────────────┬────────────────────┬──────────────────┐
│  IdentityService │   TenantService    │ AuthorizationSvc │
│                  │                    │                  │
│ CachedUserRepo   │ CachedTenantRepo   │ RedisAuthzCache  │
│ CachedSession    │ TenantSlug cache  │ UsageStore       │
│ CachedTenantCli  │ Dept cache        │ ICachedQuery     │
│ SessionCache     │ ICachedQuery       │ InvalidationCons │
│ CachedAuthzDecis  │ InvalidConsumer   │ ICachedQuery     │
│ ICachedQuery     │                    │                  │
│ InvalidConsumer  │                    │                  │
└──────────────────┴────────────────────┴──────────────────┘
```

---

# 8. Key Naming Convention

All cache keys MUST follow this canonical format:

```
<context>:<entity>:<qualifier>...:v<version>
```

## 8.1 Rules

1. **context** — The owning service or platform domain (lowercase, kebab-case): `identity`, `tenant`, `authz`, `platform`, `event-consumer`
2. **entity** — The type of data being cached (singular, lowercase, kebab-case): `user`, `session`, `tenant`, `permissions`, `decision`, `lock`, `idempotency`, `ratelimit`
3. **qualifier(s)** — Identifiers that distinguish the specific entry: tenant id, user id, slug, hash. Zero or more colon-separated segments.
4. **version** — Optional version suffix when cache format changes (`v1`, `v2`). When omitted, `v1` is implied.
5. No hyphens or underscores within segments beyond natural kebab-case. Use `N` format for Guids (`{guid:N}` = 32 hex chars, no hyphens).
6. The complete key is prefixed with the configured `CachingOptions.KeyPrefix` (default `"esp"`) at the `RedisCacheService` level. Keys in documentation omit this prefix for clarity.

## 8.2 Examples

| Owner | Key | Description |
|-------|-----|-------------|
| Infrastructure | `platform:lock:tenant-sync:v1` | Distributed lock |
| Infrastructure | `platform:idempotency:assign-role-abc123:v1` | Idempotency tracking |
| Infrastructure | `platform:ratelimit:default:192.168.1.1:v1` | Rate limit counter |
| Infrastructure | `event-consumer:authz-invalidation:50e3a8ca:v1` | Event deduplication |
| IdentityService | `identity:user:id:a1b2c3d4...:v1` | User aggregate |
| IdentityService | `identity:user:username:john.doe:v1` | User by username |
| IdentityService | `identity:session:id:a1b2c3d4...:v1` | Session aggregate |
| IdentityService | `identity:user:exists-email:john@co.com:v1` | Negative cache |
| TenantService | `tenant:tenant:id:a1b2c3d4...:v1` | Tenant aggregate |
| TenantService | `tenant:tenant:slug:contoso:v1` | Tenant by slug |
| TenantService | `tenant:department:a1b2c3d4...:v1` | Department aggregate |
| AuthorizationService | `authz:permissions:a1b2c3d4:subject-abc:v1` | Effective permissions |
| AuthorizationService | `authz:decision:a1b2c3d4:hash-xyz:v1` | Authz decision |
| AuthorizationService | `authz:quota:a1b2c3d4:subject-abc:daily:2026-01-01:v1` | Usage quota |

## 8.3 Migration

Existing keys that do not conform to this convention are grandfathered but should be migrated during cache format changes. New caches must use this convention.

---

# 9. TTL Guidelines

## 9.1 Maximum TTLs by Category

| Category | Max TTL | Rationale |
|----------|---------|-----------|
| Infrastructure (lock) | 30 seconds | Lock lease duration. Prolonged locks indicate bugs. |
| Infrastructure (idempotency) | 5 minutes | Idempotency window. Commands should not be deduplicated longer. |
| Infrastructure (rate limit) | Window duration | Always auto-expires with the window. |
| Infrastructure (dedup) | 7 days | Long window for event deduplication; 7d is conservative for at-least-once delivery envelopes. |
| Business (positive) | 5 minutes | General guideline. Must never exceed unless explicitly documented. |
| Business (security) | 30 seconds | Permission decisions, authorization data. Must not exceed 30s. |
| Business (negative) | 5 seconds | Maximum staleness for "not found" entries. |
| Computation | 5 minutes | Cost/value tradeoff. Longer TTLs require justification. |
| Computation (aggregation) | 15 minutes | Rarely-changing computed data (e.g. aggregated reports). Must document why 5 min is insufficient. |

## 9.2 Sliding vs. Absolute

- **Sliding expiration** — For caches with predictable access patterns (user profiles during a session). Resets TTL on access.
- **Absolute expiration** — For caches where staleness is measured from insert time (negative caches, security decisions). Does not extend on access.
- Rule: Business caches for mutable data should prefer sliding (active sessions keep data warm). Business caches for security decisions must use absolute (staleness measured from decision time).

## 9.3 Negative Caching

- Maximum TTL: 5 seconds.
- Must use absolute expiration, not sliding.
- Must be invalidated when the underlying entity is created (event-driven).
- The sentinel value must survive serialization. Use a dedicated marker type, not magic strings.

---

# 10. Invalidation Strategy

## 10.1 Per-Category Invalidation

### Business Cache Invalidation

| Trigger | Mechanism | Who |
|---------|-----------|-----|
| Aggregate mutation (write path) | Write-through cache removal | The handler performing the write |
| Domain event (same-service) | In-process domain event consumer | The service's `CacheInvalidationConsumer` |
| Integration event (cross-service) | MassTransit consumer + dedup guard | The subscribing service's consumer |
| TTL expiry | Redis key eviction | Automatic |
| Manual (admin action) | `InvalidateAuthorizationCacheCommand` | Authorized admin |

### Infrastructure Cache Invalidation

| Trigger | Mechanism | Notes |
|---------|-----------|-------|
| TTL expiry | Automatic | Primary invalidation method |
| Lock release | Lua-scripted | Only the lock owner can release |
| Rate limit window | ZREMRANGEBYSCORE | Sliding window algorithm |

### Computation Cache Invalidation

| Trigger | Mechanism | Who |
|---------|-----------|-----|
| TTL expiry | Automatic | Baseline |
| Source data change | Event-driven consumer | Same as business cache |
| Manual | Invalidation endpoint | Admin |

## 10.2 Event-Driven Invalidation Flow

```
Domain Event (e.g., RoleAssignedDomainEvent)
    │
    ▼
Outbox (same transaction as aggregate write)
    │
    ▼
OutboxProcessor (BackgroundService)
    │
    ▼
MassTransit (RabbitMQ)
    │
    ▼
CacheInvalidationConsumer (subscribing service)
    ├── DedupGuard (Redis: event-consumer:*)
    │   └── Skip if already processed
    │
    ▼
Cache.RemoveAsync / InvalidateByPatternAsync
```

## 10.3 InvalidationByPattern

`InvalidateByPatternAsync` (SCAN-based) must be used when:
- A tenant is disabled → invalidate ALL `identity:user:...`, `tenant:tenant:id:...`, `authz:permissions:...` for that tenant.
- A department is deleted → invalidate `tenant:department:*` and all `authz:permissions:*` referencing that department.

`InvalidateByPatternAsync` must never be called in the hot path. SCAN is an O(N) operation.

## 10.4 Startup Behavior

On startup:
- `RedisStartupTask` warms the Redis connection (PING). It does not pre-populate cache entries.
- Caches are populated lazily on first access (cache-aside).
- No "warm the world" startup procedure — the database is the source of truth.

## 10.5 TTL as Baseline, Event-Driven as Optimization

Every business cache must have a TTL. Event-driven invalidation reduces the staleness window but is not required for correctness. A cache with only a TTL (no events) is acceptable if the TTL is short enough to meet the business requirements.

Exception: Security-critical permission/authorization caches must have event-driven invalidation. The 5-second TTL is the fallback; invalidation on role/permission change is the primary mechanism.

---

# 11. Failure Strategy

## 11.1 The Platform Must Be Correct Without the Cache

This is a binding architectural rule:

> The platform must remain functionally correct if every cache is disabled.

"Correct" means:
- Valid requests are not rejected.
- Invalid requests are not accepted.
- No data loss occurs.
- No security boundary is violated.

"Correct" does not mean:
- Performance is acceptable.
- Rate limiting is enforced.
- Idempotency is guaranteed (duplicates may occur).
- Distributed locks are acquired (races may occur).

## 11.2 Failure Modes and Responses

| Failure Mode | Effect | Response |
|--------------|--------|----------|
| Cache miss | Operation proceeds without cache | Normal path; database or computation is the fallback |
| Cache unavailable (connection failed) | All operations return "miss" / "allow" | Fail open — request proceeds, logged. Readiness monitor reports degraded. |
| Redis outage | Complete cache unavailability | All operations degrade gracefully. Business operations continue against database. Rate limiting disabled. Idempotency disabled. Locks disabled. |
| Stale data | Cache returns outdated values | TTL bounds the staleness window. Event-driven invalidation reduces the window for critical data. |
| Redis high latency | Cache operations slow down requests | Fail-open after timeout. Retry policy (3 retries, exponential backoff per `RedisOptions`). |
| Connection failure during startup | RedisStartupTask fails | Service starts without Redis. Warmup retries per `StartupTaskOptions`. Connection establishes on first operation. |

## 11.3 Fail-Open Contract

All cache read operations must fail-open:
- **GET / Exists** → return `null` / `false` on failure (cache miss)
- **Rate limit check** → return `false` (allow the request) on failure
- **Distributed lock acquire** → return `null` (lock failed, caller handles) on failure
- **Idempotency check** → return `false` (allow the request) on failure
- **Deduplication guard** → return `true` (allow processing) on failure

The codebase already implements this pattern via try/catch for `RedisException` and `TimeoutException`.

## 11.4 No Retry on Read Failures

Cache reads are fast; retrying a failed read is unlikely to succeed and adds latency. The fail-open response is immediate.

Write operations (SET) may be retried once with a short delay (100ms). If the secondary write fails, the cache is inconsistent — the next read will miss and repopulate.

## 11.5 Operational Degradation

When Redis is unavailable:
- **ReadinessMonitor** reports `Healthy = false` for the cache dependency.
- All requests proceed against databases. Performance degrades (database load increases).
- Rate limiting is disabled. The API surface is effectively unbounded.
- Idempotency is disabled. Commands may execute more than once. Downstream consumers must be idempotent.
- Distributed locks are not acquired. Background job coordination falls through to best-effort.
- Event deduplication is disabled. Cache invalidation messages may be processed more than once (idempotent by design).

---

# 12. Security Considerations

### 12.1 Cache Poisoning

A cache hit for a poisoned entry causes incorrect behavior. Mitigations:
- Cache entries are written only by the owning service through controlled code paths.
- Cache keys are constructed from validated identifiers (Guids, known slugs).
- No user-supplied string is inserted directly into a key without validation.

### 12.2 Information Leakage

The same Redis instance serves all services and cache categories. Mitigations:
- Key prefix ensures no accidental key collision between services.
- `authz:decision:*` caches only ALLOW decisions (never DENY). A cache miss defaults to deny, so eviction cannot cause unauthorized access.
- Permission caches are tenant-scoped: `authz:permissions:{tenantId}:{subjectId}`.
- Negative caches reveal only that a resource does not exist — no sensitive data.

### 12.3 Cache and Authorization

- Authorization decisions are cached with TTL = 5 seconds. A stale ALLOW is bounded to 5s. A stale DENY is impossible — only ALLOW outcomes are cached.
- A cache miss for an authorization decision results in re-evaluation (full policy engine). The worst case is 5s of stale ALLOW after a permission change.

### 12.4 Cache and Tenant Isolation

Cache keys always include tenant ID (`{tenantId:N}`) for tenant-scoped entries. This prevents cross-tenant cache overlap.

Exception: Infrastructure caches (`lock:*`, `idempotency:*`, `ratelimit:*`) are not tenant-scoped because they are coordination primitives, not tenant data.

### 12.5 Redis Connection Security

- Redis connection is not authenticated in the current configuration (`abortConnect=false`). Production deployments must configure `RedisOptions.Password` and use TLS.
- `IRedisConnectionFactory` is internal to Platform — services cannot open raw Redis connections. Exception: `RedisRealTimeUsageStore` and `MonthlyQuotaResetJob` use `IConnectionMultiplexer` directly — these should be reviewed for compliance.

---

# 13. Operational Considerations

### 13.1 Observability

Every cache operation must emit:
- **Cache hit/miss** — counter or log per key context
- **Cache latency** — histogram per operation (GET, SET, DEL, SCAN)
- **Invalidation events** — log per key invalidated and the triggering event
- **Cache size** — approximate key count per context (via SCAN or INFO keyspace)
- **Redis health** — `ReadinessMonitor` PING, connection status events

### 13.2 Key Count Monitoring

Platform.Caching should expose:
- Approximate key count per prefix (via `SCAN 0 MATCH esp:* COUNT 10000`)
- Memory usage per prefix (via `MEMORY USAGE`)
- Expired key rate (via `INFO stats`)

### 13.3 Cache Configuration Management

- All TTLs must be configurable via `IConfiguration` / `IOptions<T>`.
- All caches must have a feature toggle (`Enabled = true/false`) so that caches can be disabled per-deployment without code changes.
- `AuthorizationCacheOptions.Enabled` is a good pattern — every cache should follow it.

### 13.4 Cluster Considerations

- `RedisRealTimeUsageStore` uses hash tags (`{{hashSlot}}`) for cluster compatibility.
- `InvalidateByPatternAsync` (SCAN) works on a single node. In cluster mode, it must iterate over all nodes. `Platform.Caching.RedisCacheService` currently connects to a single endpoint — this does not support cluster.
- Distributed locks and rate limiters use a single key — cluster-safe.
- Future: if the Redis deployment is clustered, SCAN-based invalidation must be updated to iterate all nodes.

### 13.5 Connection Management

- `RedisConnectionFactory` uses a lazy singleton `ConnectionMultiplexer` (thread-safe).
- Connection failed/restored events are logged.
- Startup validates connection with PING before declaring readiness.
- Connection timeout: 5000ms. Retry: 3 attempts.

---

# 14. Consequences

## Positive

- **Clear ownership** — Every cache entry has a single owner. No ambiguity about who maintains, invalidates, or is responsible for a cache.
- **Platform purity** — Platform caches infrastructure only. Business logic stays in services where it belongs.
- **Canonical key convention** — Every cache key following `<context>:<entity>:<qualifier>:v<version>` enables tooling, pattern-based invalidation, and operational understanding.
- **Fail-safe by contract** — The platform's correctness does not depend on the cache. Redis can go down without causing incorrect behavior.
- **Explicit invalidation** — Every business cache must document how it is invalidated, eliminating the "TTL-only as default" anti-pattern.
- **Security** — Cache poisoning and information leakage risks are mitigated through key design, scoping, and ALLOW-only caching.

2. **Governance.** All new cache implementations must be reviewed against this ADR. A cache that does not conform to the category model, ownership rules, or key convention must be rejected.

3. **Refactoring target.** Several existing caches require alignment:
   - `RedisRealTimeUsageStore` directly uses `IConnectionMultiplexer` — should use `IDistributedCacheService`.
   - `MonthlyQuotaResetJob` directly uses `IConnectionMultiplexer` — should use `IDistributedCacheService`.
   - `CachedTenantServiceClient.NotFoundSentinel` is defined but never read — negative caching is broken.
   - `CachedAuthorizationDecisionService` caches business data in IdentityService — this is acceptable per the ownership matrix (owned by IdentityService for resolution flows) but must document its invalidation strategy.
   - `SessionCache` (IMemoryCache) is in-process; cache is lost on service restart. This is acceptable for session activity tracking (ephemeral data).

---

# 15. Future Work

1. **Cache Key Registry** — Maintain a machine-readable registry of all cache keys, their owners, TTLs, and invalidation strategies (similar to ADR-009 §Cache Key Registry but extended with ownership).

2. **Cluster Support for SCAN** — Update `RedisCacheService.InvalidateByPatternAsync` to iterate all nodes in a cluster deployment.

3. **ICachedQuery Governance** — Introduce a constraint that prevents ICachedQuery from caching certain data types without explicit platform-level approval.

4. **Cache Size Limits** — Introduce per-service key count limits or memory limits via Redis `maxmemory-policy`.

5. **Automated Key Naming Lint Check** — Add an analyzer that flags cache keys not following the `<context>:<entity>:<qualifier>:v<version>` convention, plus forced evaluation of PolicyService cache keys.

6. **ADR-009 Phase 2 Implementation** — Wire `AuthorizationCacheInvalidationConsumer` to MassTransit (ADR-009 identified this as never wired). This remains open work.

7. **Standalone PolicyService Caching** — When PolicyService is designed (ADR-011 Phase 3), its caching strategy must comply with this ADR.

---

# Appendix A: Cache Inventory

The following inventory was produced by a comprehensive codebase audit covering 91 files across all projects.

## A.1 Infrastructure Caches (Platform)

| File | Type | Content | Key Format | TTL | Owner |
|------|------|---------|-----------|-----|-------|
| `Platform.Caching/RedisConnectionFactory.cs` | Connection state | Redis `ConnectionMultiplexer` lifecycle | N/A | N/A | Platform.Caching |
| `Platform.Caching/RedisCacheService.cs` | Generic distributed cache | Any serializable `T` | `{prefix}:{key}` | Configurable (default 60s) | Platform.Caching |
| `Platform.Caching/RateLimiting/RedisDistributedRateLimiterService.cs` | Rate limiting | Sorted-set sliding window counters | `ratelimit:{policy}:{key}` | Window-based auto-expire | Platform.Caching |
| `Platform.Caching/Locking/RedisDistributedLockService.cs` | Distributed lock | SET NX lock ownership | `lock:{name}` | Default 30s | Platform.Caching |
| `Platform.Caching/Idempotency/DistributedIdempotencyStore.cs` | Idempotency | In-progress/completed state | `idempotency:{key}` | 5 min | Platform.Caching |
| `Platform.Caching/Idempotency/RedisEventConsumerDeduplicationGuard.cs` | Event dedup | Processed event IDs | `event-consumer:{name}:{eventId:N}` | Per-call (default 7d) | Platform.Caching |
| `Platform.Infrastructure/RedisStartupTask.cs` | Warmup | Redis PING result | N/A | N/A | Platform.Infrastructure |
| `Platform.Infrastructure/ReadinessMonitor.cs` | Health | Redis connectivity | N/A | N/A | Platform.Infrastructure |

## A.2 Business Caches (Services)

### AuthorizationService

| File | Content | Key Format | TTL | Invalidation |
|------|---------|-----------|-----|--------------|
| `RedisAuthorizationCache.cs` | Effective permissions, ALLOW decisions | `authz:{tenantId}:subject:{subjectId}:effective-permissions`, `authz:{tenantId}:decision:{hash}` | 5s | Event-driven (role/permission events) + TTL |
| `RedisRealTimeUsageStore.cs` | Quota/usage counts | `quota:{{hashSlot}}:{tenantId}:{subjectId}:{action}:{resourceType}:{resourceId}:{daily/monthly}:{yyyy-MM-dd}` | Window-based | MonthlyQuotaResetJob |
| `EffectivePermissionResolver.cs` | Wraps `RedisAuthorizationCache` | Same as above | 5s | Same as above |
| `GetEffectivePermissionsQuery.cs` (ICachedQuery) | Effective permission sets | `effective-permissions:{SubjectId:N}` | 5 min | TTL only |
| `CacheInvalidationConsumer.cs` | In-process domain event invalidation | N/A | N/A | Event-driven |
| `AuthorizationCacheInvalidationConsumer.cs` (MassTransit) | Cross-service cache invalidation | N/A | N/A | Event-driven |

### IdentityService

| File | Content | Key Format | TTL | Invalidation |
|------|---------|-----------|-----|--------------|
| `CachedUserRepository.cs` | User aggregates (`UserCacheDto`) | `identity:user:id:{tenantId:N}:{userId:N}`, `identity:user:username:{tenantId:N}:{username}`, `identity:user:exists-username:{tenantId:N}:{username}`, `identity:user:email:{tenantId:N}:{email}`, `identity:user:exists-email:{tenantId:N}:{email}`, `identity:user:phone:{tenantId:N}:{phone}`, `identity:user:exists-phone:{tenantId:N}:{phone}` | 30s positive, 3s negative | Write-through + TTL |
| `CachedSessionRepository.cs` | Session aggregates | `identity:session:id:{tenantId:N}:{sessionId:N}` | 30s positive, 3s negative | Write-through + TTL |
| `CachedTenantServiceClient.cs` | Tenant slug→id map (`SharedTenantCacheDto`) | `platform:shared:tenant:slug:{slug}` | 5 min | Event-driven (TenantCreatedV1) |
| `CachedAuthorizationDecisionService.cs` | Platform admin authz decisions | `authz:platform:admin:{tenantId:N}:{subjectId:N}` | 10 min | TTL only |
| `SessionCache.cs` (IMemoryCache) | Session activity | `identity:session:v1:{tenantId}:{sessionId}` | 5 min sliding | TTL (in-process) |
| `GetUserQuery.cs` (ICachedQuery) | User query result | `user:{UserId:N}` | 5 min | TTL only |
| `TenantCreatedCacheInvalidationConsumer.cs` (MassTransit) | Invalidates slug cache | N/A | N/A | Event-driven |

### TenantService

| File | Content | Key Format | TTL | Invalidation |
|------|---------|-----------|-----|--------------|
| `CachedTenantRepository.cs` | Tenant aggregates, slug DTOs | `tenant-service:tenant:id:{id:N}`, `tenant-service:tenant:code:{code}`, `platform:shared:tenant:slug:{slug}` | 30s sliding | Write-through + TTL |
| `GetTenantBySlugQuery.cs` (ICachedQuery) | Tenant query result | `tenant-service:by-slug:{slug}` | 5 min | TTL only |
| `ListDepartmentsQuery.cs` (ICachedQuery) | Department list | `departments:{TenantId:N}` | 5 min | Event-driven + TTL |
| `GetDepartmentByIdQuery.cs` (ICachedQuery) | Department query | `department:{TenantId:N}:{DepartmentId:N}` | 5 min | Event-driven + TTL |
| `TenantCacheInvalidationConsumer.cs` (MassTransit) | Cross-service cache invalidation | N/A | N/A | Event-driven |

## A.3 ICachedQuery Implementations

| Query | Key | TTL | Registration Status | Category |
|-------|-----|-----|-------------------|----------|
| `GetEffectivePermissionsQuery` | `effective-permissions:{SubjectId:N}` | 5 min | Dead (no CachingBehavior registration per ADR-009) | Computation |
| `GetUserQuery` | `user:{UserId:N}` | 5 min | Working | Computation |
| `GetTenantBySlugQuery` | `tenant-service:by-slug:{slug}` | 5 min | Dead (no CachingBehavior registration per ADR-009) | Computation |
| `ListDepartmentsQuery` | `departments:{TenantId:N}` | 5 min | Dead (no CachingBehavior registration per ADR-009) | Computation |
| `GetDepartmentByIdQuery` | `department:{TenantId:N}:{DepartmentId:N}` | 5 min | Dead (no CachingBehavior registration per ADR-009) | Computation |

## A.4 Idempotent Commands

| Command | Idempotency Key |
|---------|----------------|
| `AssignRoleCommand` | `assign-role:{SubjectId:N}:{RoleId:N}` |
| `RevokeRoleCommand` | `revoke-role:{SubjectId:N}:{RoleId:N}` |
| `GrantPermissionCommand` | `grant-permission:{RoleId:N}:{PermissionId:N}` |
| `RevokePermissionCommand` | `revoke-permission:{RoleId:N}:{PermissionId:N}` |
| `ChangeUserRoleCommand` | `change-user-role:{SubjectId:N}:{NewRoleId:N}` |
| `CreateRoleCommand` | `create-role:{DepartmentId:N}:{Name}` |
| `CreatePermissionCommand` | `create-permission:{Key}` |
| `RegisterCommand` | `register:{PhoneNumber}` |
| `DeleteUserCommand` | `delete-user:{UserId:N}` |
| `ActivateUserCommand` | `activate-user:{UserId:N}` |
| `DisableUserCommand` | `disable-user:{UserId:N}` |
| `UnlockUserCommand` | `unlock-user:{UserId:N}` |
| `CreateTenantCommand` | `create-tenant:{CorrelationId:N}` |
| `UpdateTenantPlanCommand` | `update-tenant-plan:{TenantId:N}:{NewPlanTier}` |
| `ChangeTenantStatusCommand` | `change-tenant-status:{CorrelationId:N}` |
| `CreateDepartmentCommand` | `create-department:{CorrelationId:N}` |
| `UpdateDepartmentCommand` | `update-department:{CorrelationId:N}` |
| `DeleteDepartmentCommand` | `delete-department:{CorrelationId:N}` |

## A.5 Background Jobs Touching Cache

| Job | Project | Action | Cache Touched |
|-----|---------|--------|---------------|
| `OutboxProcessorBase` (abstract) | Platform.Infrastructure | Publishes events consumed by cache invalidation consumers | Indirect — events drive invalidation |
| `TenantOutboxProcessor` | TenantService.Infrastructure | Publishes TenantCreated/StatusChanged/PlanUpgraded events | Invalidates tenant + department caches |
| `OutboxProcessor` | AuthorizationService.Infrastructure | Publishes Role/Permission events | Invalidates authorization caches |
| `OutboxProcessor` | IdentityService.Infrastructure | Publishes User lifecycle events | Invalidates user/session caches |
| `MonthlyQuotaResetJob` | AuthorizationService.Infrastructure | Scans and resets monthly quota keys | `quota:*:monthly:*` |
| `ReadinessMonitor` | Platform.Infrastructure | PING Redis periodically | Connection state |
| `RedisStartupTask` | Platform.Infrastructure | PING Redis at startup | Connection state |

---

# Appendix B: Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Business data cached in Platform | Low (none found) | Medium — ownership boundaries violated | ADR-016 forbids this; code review gate |
| Negative cache produces false "not found" | Medium | High — prevents entity creation | Short TTL (3s); invalidate on entity create events |
| `ICachedQuery` dead code | High (4 of 5 per ADR-009) | Medium — stale cached data | Fix DI registration gaps (ADR-009 Phase 1) |
| `AuthorizationCacheInvalidationConsumer` never wired | High (per ADR-009) | Medium — stale authorization data | Wire to MassTransit (ADR-009 Phase 2) |
| Cache key collision (same Redis) | Low | High — cross-service data corruption | Key prefix (`esp:`), tenant+service scoping |
| Redis outage | Medium | High — performance degradation, no rate limiting, no idempotency | Fail-open by contract; database is source of truth |
| Cache stampede on TTL expiry | Medium | Medium — database load spike | `RedisCacheService.GetOrCreateAsync` uses `SemaphoreSlim` stampede protection |
| Cluster incompatibility (SCAN) | Medium | Medium — pattern-based invalidation fails in cluster | Documented limitation; future work item |
| `RedisRealTimeUsageStore` bypasses Platform.Caching | Medium | Low — uses raw `IConnectionMultiplexer` | Acceptable for hash-tagged cluster compatibility |
| SessionCache (IMemoryCache) lost on restart | Medium | Low — all sessions revalidated | Ephemeral data; acceptable for session activity tracking |
| Rate limit disabled during Redis outage | Medium | High — unbounded API traffic | Fail-open is correct; alert on Redis health |
| SharedKernel.CacheKeyBuilder defines business keys tenancy | Low | Low — abstractions are not caches | OK — SharedKernel provides utilities, not stores |

---

# Appendix C: Key Convention Migration Guide

Existing keys do not all conform to the `<context>:<entity>:<qualifier>:v<version>` convention. The following table documents current vs. target formats:

| Current Pattern | Target Pattern | Owner |
|----------------|----------------|-------|
| `tenant:slug:{slug}` | `tenant:slug:{slug}` (conforms) | TenantService |
| `tenant:metadata:{tenantId:N}` | `tenant:metadata:{tenantId:N}` (conforms) | TenantService |
| `tenant:config:{tenantId:N}` | `tenant:config:{tenantId:N}` (conforms) | TenantService |
| `tenant:feature:{tenantId:N}` | `tenant:feature:{tenantId:N}` (conforms) | TenantService |
| `tenant:limit:{tenantId:N}` | `tenant:limit:{tenantId:N}` (conforms) | TenantService |
| `authz:decision:{tenantId:N}:{hash}` | `authz:decision:{tenantId:N}:{hash}:v1` | AuthorizationService |
| `authz:permissions:{tenantId:N}:{subjectId}` | `authz:permissions:{tenantId:N}:{subjectId}:v1` | AuthorizationService |
| `policy:evaluation:{tenantId:N}:{policyId}` | `policy:evaluation:{tenantId:N}:{policyId}:v1` | Future PolicyService |
| `session:{tenantId:N}:{sessionId}` | `identity:session:{tenantId:N}:{sessionId}:v1` | IdentityService |
| `lock:{name}` | `platform:lock:{name}:v1` | Platform |
| `idempotency:{key}` | `platform:idempotency:{key}:v1` | Platform |
| `ratelimit:{policyName}:{key}` | `platform:ratelimit:{policyName}:{key}:v1` | Platform |
| `identity:user:id:{tenantId:N}:{userId:N}` | Conforms as-is | IdentityService |
| `identity:user:username:{tenantId:N}:{username}` | Conforms as-is | IdentityService |
| `identity:user:exists-username:{tenantId:N}:{username}` | Conforms as-is (negative cache) | IdentityService |
| `identity:session:id:{tenantId:N}:{sessionId:N}` | Conforms as-is | IdentityService |
| `platform:shared:tenant:slug:{slug}` | `tenant:slug:{slug}:shared:v1` | TenantService (shared cross-service) |
| `authz:platform:admin:{tenantId:N}:{subjectId:N}` | `authz:admin-check:{tenantId:N}:{subjectId:N}:v1` | AuthorizationService |
| `tenant-service:tenant:id:{id:N}` | `tenant:tenant:{id:N}:v1` | TenantService |
| `tenant-service:tenant:code:{code}` | `tenant:code:{code}:v1` | TenantService |
| `event-consumer:{consumerName}:{eventId:N}` | `platform:dedup:{consumerName}:{eventId:N}:v1` | Platform |
| `identity:session:v1:{tenantId}:{sessionId}` | Conforms as-is (IMemoryCache) | IdentityService |
| `effective-permissions:{SubjectId:N}` | `authz:effective-permissions:{SubjectId:N}:v1` | AuthorizationService |
| `user:{UserId:N}` | `identity:user:query:{UserId:N}:v1` | IdentityService |
| `tenant-service:by-slug:{slug}` | `tenant:slug:{slug}:query:v1` | TenantService |
| `departments:{TenantId:N}` | `tenant:departments:{TenantId:N}:v1` | TenantService |
| `department:{TenantId:N}:{DepartmentId:N}` | `tenant:department:{TenantId:N}:{DepartmentId:N}:v1` | TenantService |
| `quota:{{hashSlot}}:...` | `authz:quota:{{hashSlot}}:...:v1` | AuthorizationService |

Note: Key migration is deferred to future cache format changes. New caches must use the target format.

---

# Appendix D: SharedKernel.CacheKeyBuilder Usage Audit

`SharedKernel.Caching.CacheKeyBuilder` defines 10 static key helpers used across services:

| Method | Used By | Count |
|--------|---------|-------|
| `TenantSlug(slug)` | TenantService | 0 |
| `TenantMetadata(tenantId)` | 0 | 0 |
| `TenantConfig(tenantId)` | 0 | 0 |
| `TenantFeature(tenantId)` | 0 | 0 |
| `TenantLimit(tenantId)` | 0 | 0 |
| `AuthorizationDecision(tenantId, hash)` | 0 | 0 |
| `EffectivePermissions(tenantId, subjectId)` | 0 | 0 |
| `PolicyEvaluation(tenantId, policyId)` | 0 | 0 |
| `Session(tenantId, sessionId)` | 0 | 0 |
| `DistributedLock(name)` | Platform.Caching | Yes (in RedisDistributedLockService) |
| `Idempotency(key)` | Platform.Caching | Yes (in DistributedIdempotencyStore) |
| `RateLimit(policyName, key)` | Platform.Caching | Yes (in RedisDistributedRateLimiterService) |
| `Combine(params)` | IdentityService (CachedTenantServiceClient), TenantService (CachedTenantRepository) | Multiple |

Findings:
- 6 of 12 helpers are never called (dead code): `TenantSlug`, `TenantMetadata`, `TenantConfig`, `TenantFeature`, `TenantLimit`, `AuthorizationDecision`, `EffectivePermissions`, `PolicyEvaluation`, `Session`.
- The helpers that are used (`DistributedLock`, `Idempotency`, `RateLimit`, `Combine`) are infrastructure-oriented.
- Services build their own keys inline or via service-specific `CacheKeyBuilder` classes rather than using the shared helpers.

Recommendation: Remove unused helpers from `SharedKernel.CacheKeyBuilder`, or repurpose as a canonical registry. The unused helpers represent a design intent that was never realized — they were added before the ownership model was established and assumed SharedKernel would own business key formats. Per this ADR, SharedKernel owns abstractions only. Business key formats belong to their respective services.