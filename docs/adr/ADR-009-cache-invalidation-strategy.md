# ADR-009

Title:

ICachedQuery Pipeline Cache Invalidation Strategy

Status:

Accepted

---

# Context

13 `ICachedQuery` implementations exist across 5 services. Analysis reveals:

1. **Only 1 works** — `GetUserQuery` in IdentityService (via `CachingBehavior`)
2. **4 would crash** — PolicyService (5 queries) and AuditService (4 queries) lack `IDistributedCacheService` registration
3. **3 are dead code** — TenantService (2 queries) and AuthorizationService (1 query) lack `CachingBehavior` registration
4. **Event-driven invalidation exists** but `AuthorizationCacheInvalidationConsumer` is never wired to MassTransit
5. **`InvalidateByPatternAsync`** is implemented but never called
6. All queries use TTL-only with no invalidation strategy

---

# Decision

Implement a **hybrid strategy**: TTL as baseline + event-driven invalidation for write-heavy queries + fix all DI registration gaps.

---

# Rationale

### Why not TTL-only?

- 5-minute stale window for authorization permissions is unacceptable
- Cache stampede risk on popular keys after TTL expiry
- No way to proactively invalidate on data change

### Why not pure event-driven?

- Read-heavy queries (audit logs, policy lists) rarely change — TTL is sufficient
- Event-driven adds complexity without proportional benefit for immutable data
- Over-invalidation causes unnecessary cache misses

### Why hybrid?

- **Authorization permissions** (write-heavy): event-driven invalidation (already partially implemented)
- **Tenant data** (write-medium): event-driven invalidation (already implemented in `TenantCacheInvalidationConsumer`)
- **User data** (write-medium): event-driven invalidation (simple, few keys)
- **Policy data** (write-medium): event-driven invalidation + TTL
- **Audit data** (write-heavy, append-only): TTL-only (immutable, no invalidation needed)

---

# Implementation Plan

## Phase 1: Fix DI Registration Gaps (CRITICAL)

### PolicyService

Add `AddPlatformCaching(configuration)` to `DependencyInjection.cs`:

```csharp
// PolicyService.Infrastructure/DependencyInjection.cs
services.AddPlatformCaching(configuration);  // ADD THIS
```

### AuditService

Add `AddPlatformCaching(configuration)` to `DependencyInjection.cs`:

```csharp
// AuditService.Infrastructure/DependencyInjection.cs
services.AddPlatformCaching(configuration);  // ADD THIS
```

### TenantService

`AddPlatformBehaviors` is already registered. Verify `CachingBehavior` is included.

### AuthorizationService

`AddPlatformBehaviors` is already registered. Verify `CachingBehavior` is included.

## Phase 2: Wire AuthorizationCacheInvalidationConsumer

### Current State

`AuthorizationCacheInvalidationConsumer` exists but is never registered in MassTransit.

### Fix

```csharp
// AuthorizationService.Infrastructure/DependencyInjection.cs
cfg.AddConsumer<AuthorizationCacheInvalidationConsumer>();
```

## Phase 3: Event-Driven Invalidation for ICachedQuery

### AuthorizationService

**Query:** `GetEffectivePermissionsQuery` — key: `effective-permissions:{SubjectId}:{TenantId}`
**Events:** `RoleAssignedV1`, `RoleRevokedV1`, `PermissionGrantedV1`, `PermissionRevokedV1`
**Action:** Invalidate specific key when role/permission changes

```csharp
// In CacheInvalidationConsumer or AuthorizationCacheInvalidationConsumer
case "RoleAssignedV1":
case "RoleRevokedV1":
case "PermissionGrantedV1":
case "PermissionRevokedV1":
    var subjectId = eventPayload.SubjectId;
    var tenantId = eventPayload.TenantId;
    await cache.RemoveAsync($"effective-permissions:{subjectId}:{tenantId}");
    break;
```

### TenantService

**Query:** `GetDepartmentByIdQuery`, `ListDepartmentsQuery`
**Events:** `DepartmentCreatedV1`, `DepartmentStatusChangedV1`, `DepartmentNameUpdatedV1`
**Action:** Already handled by `TenantCacheInvalidationConsumer` — verify department cache keys are invalidated

### IdentityService

**Query:** `GetUserQuery` — key: `user:{UserId}`
**Events:** `UserActivatedV1`, `UserDisabledV1`, `UserDepartmentChangedV1`
**Action:** Invalidate user cache on status/department change

### PolicyService

**Query:** `GetPolicyQuery`, `ListPoliciesQuery`
**Events:** `PolicyPublishedV1`, `PolicyDeprecatedV1`, `PolicyArchivedV1`, `PolicyRuleAddedV1`, `PolicyRuleRemovedV1`
**Action:** Invalidate policy cache on state change

### AuditService

**Query:** All search queries are append-only (immutable data)
**Action:** TTL-only — no event-driven invalidation needed (data never changes)

## Phase 4: TTL Tuning

| Query Type | TTL | Rationale |
|------------|-----|-----------|
| Authorization permissions | 30 seconds | Write-heavy, security-critical |
| Tenant data | 5 minutes | Write-medium, eventual consistency acceptable |
| User data | 5 minutes | Write-medium |
| Policy data | 5 minutes | Write-medium |
| Audit search | 2 minutes | Append-only, high volume |
| Policy list | 2 minutes | High volume, changes infrequently |

## Phase 5: Implement InvalidateByPatternAsync Usage

Use existing `InvalidateByPatternAsync` for bulk invalidation scenarios:

```csharp
// When a tenant is disabled, invalidate ALL cached data for that tenant
await cache.InvalidateByPatternAsync($"*:{tenantId}:*");
```

---

# Cache Key Registry

| Key Pattern | TTL | Service | Invalidation Trigger |
|-------------|-----|---------|---------------------|
| `user:{UserId}` | 5 min | Identity | UserActivated, UserDisabled, UserDepartmentChanged |
| `effective-permissions:{SubjectId}:{TenantId}` | 30 sec | Authorization | RoleAssigned, RoleRevoked, PermissionGranted, PermissionRevoked |
| `department:{TenantId}:{DepartmentId}` | 5 min | Tenant | DepartmentCreated, DepartmentStatusChanged |
| `departments:{TenantId}` | 5 min | Tenant | DepartmentCreated, DepartmentStatusChanged |
| `policy:{PolicyId}` | 5 min | Policy | PolicyPublished, PolicyDeprecated, PolicyRuleAdded/Removed |
| `policies:{params}` | 2 min | Policy | PolicyCreated, PolicyPublished, PolicyDeleted |
| `audit-record:{Id}` | 5 min | Audit | None (immutable) |
| `audit-logs:{params}` | 2 min | Audit | None (immutable) |

---

# Consequences

Positive:

- Fixes all 13 broken/dead ICachedQuery implementations
- Security-critical authorization permissions invalidated within 30 seconds
- Audit data remains immutable (no unnecessary invalidation)
- Backward compatible — TTL always works as fallback
- Uses existing infrastructure (`InvalidateByPatternAsync`)

Negative:

- Hybrid approach adds complexity
- Need to maintain cache key registry
- Event-driven invalidation adds message processing overhead

---

# Validation Criteria

1. All 5 services build successfully
2. `CachingBehavior` registered in all 5 services
3. `AuthorizationCacheInvalidationConsumer` wired to MassTransit
4. Permission cache invalidated within 30 seconds of role/permission change
5. No runtime crashes from missing `IDistributedCacheService`
6. Cache hit rate > 80% for read-heavy queries
