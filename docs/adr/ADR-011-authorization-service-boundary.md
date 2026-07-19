# ADR-011: AuthorizationService Domain Model and Aggregate Design

## Status

Accepted (final)

## Context

AuthorizationService manages Role-Based Access Control for an internal enterprise platform. The authoritative business domain defines the organizational hierarchy:

```
Tenant
  └── Departments
        └── Roles
              └── Permissions
```

A Department contains multiple Roles. A Role contains multiple Permissions. A User belongs to exactly one Tenant, exactly one Department, and has exactly one Role. These are fundamental domain relationships.

The Domain Model must represent this hierarchy. Service boundaries adapt to the Domain Model, not the opposite. Organizational hierarchy belongs to the Domain Model. PolicyService evaluates policies — it does not model organizational structure.

### Current Misalignments

1. **Role lacks DepartmentId.** Role is scoped to Tenant only. All Departments in a Tenant share a flat Role namespace. Two Departments cannot create distinct Roles with the same name.

2. **RoleAssignment redundantly stores DepartmentId.** Since Role belongs to a Department, the Department is derivable from the assigned Role.

3. **"User has exactly one Role" is not enforced.** Multiple active RoleAssignments are possible.

4. **Role uniqueness is tenant-wide, not department-scoped.** The database enforces `(TenantId, Name) UNIQUE`.

5. **UsageTracking and quota infrastructure live inside AuthorizationService.** The business domain assigns these to the future PolicyService.

6. **RoleStatus has no Disabled state.** When a Department becomes inactive or is soft-deleted, its Roles have no designated status to reflect this.

7. **No TenantService integration for Department validation.** CreateRole does not validate that the referenced Department exists.

---

## Architecture Principles

These principles govern every decision in this ADR:

1. The Business Domain has higher priority than service decomposition.
2. Service boundaries adapt to the Domain Model, not the opposite.
3. Department is part of the Domain Model.
4. Department is NOT owned by AuthorizationService.
5. DepartmentId is a stable external identity reference.
6. Role belongs to exactly one Department.
7. A User has exactly one Role.
8. Organizational hierarchy belongs to the Domain Model.
9. PolicyService evaluates policies. It does not model organizational structure.
10. Authorization must not introduce runtime coupling with TenantService during authorization evaluation.

---

## Decision

### 1. Role Contains DepartmentId — Immutable Domain Identity Reference

**The business rule "Every Department contains multiple Roles" is a fundamental domain relationship.** It is not an authorization policy. It is not enforced by PolicyService. It exists in the Domain Model.

Role contains `DepartmentId` as an external identity reference. This `Guid` identifies which Department owns the Role. AuthorizationService does NOT own the Department aggregate. TenantService remains the single source of truth for Department lifecycle and existence.

**DepartmentId is immutable.** Once a Role is created with a DepartmentId, it never changes. Moving a Role between Departments is not supported. If the business ever requires this capability, it must be implemented as a dedicated business use case, not by updating DepartmentId. The aggregate should expose no method to change DepartmentId.

DepartmentId on Role serves three purposes:
- Scopes Role uniqueness to the owning Department
- Enables Department-level Role queries (e.g., "list Roles for Department X")
- Makes the organizational hierarchy explicit in the domain model

### 2. Department Validation Through TenantService Integration

AuthorizationService does NOT validate Department existence inside the Role aggregate. The Role aggregate assumes that DepartmentId has already been validated by the integration workflow before Role creation.

The validation workflow for write operations is:

```
AuthorizationService (CreateRole command)
        ↓
TenantService Integration (validate Department exists and is active)
        ↓
Role.Create(tenantId, departmentId, name, ...)
```

**This validation applies ONLY to write operations** such as CreateRole and any future business operations that require validating a Department.

**Authorization decisions must NEVER synchronously call TenantService.** Permission evaluation, RBAC resolution, and authorization decisions execute entirely inside AuthorizationService without synchronous dependency on TenantService. If Department information is required during authorization, it must already exist inside AuthorizationService through local persistence, cache, or asynchronous synchronization. Authorization must remain operational even if TenantService is temporarily unavailable.

### 3. Remove DepartmentId from RoleAssignment

Since Role contains DepartmentId, the Department for a given assignment is derivable:

```
RoleAssignment
  → Role
    → DepartmentId
```

Storing DepartmentId on RoleAssignment is redundant. The Department is a property of the Role, not of the assignment.

### 4. Role Uniqueness Must Be Department-Scoped

Current uniqueness `(TenantId, Name)` is incorrect. Two Departments in the same Tenant would collide on Role names.

The required uniqueness constraint is `(TenantId, DepartmentId, Name)`.

### 5. Enforce "One User Has Exactly One Role"

This is a core business invariant. A User has exactly one Role. This is not optional and not a policy concern.

**Enforcement strategy — defense in depth:**

**Application layer:** Before creating a `RoleAssignment`, query `IRoleAssignmentRepository` for any existing active assignment for the SubjectId. Reject if one exists.

**Database layer:** Create a unique partial index on `authorization_role_assignments` where `RevokedAtUtc IS NULL` for `(TenantId, SubjectId)`. This prevents multiple active assignments regardless of application logic.

Both layers must be implemented. The database index is the safety net; the application check provides a clear business error message.

### 6. RoleAssignment Remains an AggregateRoot

RoleAssignment has an independent lifecycle:
- Created via `RoleAssignment.Assign()` factory
- Revoked via `Revoke()` method
- Queried independently by SubjectId or RoleId
- Has its own repository `IRoleAssignmentRepository`
- Emits `RoleAssignedDomainEvent` and `RoleRevokedDomainEvent`

RoleAssignment is NOT a child entity of Role. It is a separate aggregate that references Role by RoleId.

### 7. RoleStatus Gains a Disabled State

When a Department becomes inactive or is soft-deleted, all Roles belonging to that Department become Disabled. Disabled Roles:
- Cannot be assigned to Users
- Cannot participate in authorization decisions
- Remain persisted for audit and history
- Keep their original DepartmentId

Roles are never automatically moved to another Department. Soft delete of a Department must preserve historical consistency — Disabled Roles retain their DepartmentId.

The `RoleStatus` enum must be extended: `Active`, `Inactive`, `Disabled`.

The distinction between Inactive and Disabled:
- **Inactive** — an administrator explicitly deactivated the Role
- **Disabled** — the owning Department is no longer active; Role is disabled as a consequence

### 8. Extract UsageTracking to PolicyService (Phase 3)

The business domain assigns usage tracking, quotas, and rate limiting to PolicyService. These are extraction candidates for when PolicyService is designed. No changes now.

### 9. Simplify Permission Lifecycle (Phase 2)

The 6-state lifecycle is governance overhead for an internal platform. Simplification is a Phase 2 activity.

### 10. Downgrade PermissionGrant to Entity (Phase 2)

PermissionGrant has no independent lifecycle. This is a Phase 2 cleanup.

---

## Aggregate Boundaries

### Aggregate Design

```
AuthorizationService.Domain.Aggregates/
│
├── Role (AggregateRoot)
│   ├── TenantId          — inherited from Entity (RLS)
│   ├── DepartmentId      — immutable external identity reference
│   ├── Name              — RoleName (value object)
│   ├── Description       — string
│   ├── Status            — RoleStatus (Active / Inactive / Disabled)
│   ├── ParentRoleId      — RoleId? (hierarchy)
│   └── HierarchyDepth    — int
│
├── Permission (AggregateRoot)
│   ├── TenantId          — inherited from Entity (RLS)
│   ├── Key               — PermissionKey (value object)
│   ├── Action            — AuthorizationAction (value object)
│   ├── ResourceType      — string
│   ├── Description       — string
│   ├── OwnerTeam         — string
│   ├── Lifecycle         — PermissionLifecycle (6 states)
│   └── PermissionVersion — int
│
├── RoleAssignment (AggregateRoot)
│   ├── TenantId          — inherited from Entity (RLS)
│   ├── SubjectId         — SubjectId (references User in IdentityService)
│   ├── RoleId            — RoleId (references Role)
│   ├── AssignedBy        — UserId
│   ├── AssignedAtUtc     — DateTimeOffset
│   └── RevokedAtUtc      — DateTimeOffset?
│
├── PermissionGrant (AggregateRoot — candidate for Entity in Phase 2)
│   ├── TenantId          — inherited from Entity (RLS)
│   ├── RoleId            — RoleId (references Role)
│   ├── PermissionId      — PermissionId (references Permission)
│   ├── GrantedBy         — UserId
│   ├── GrantedAtUtc      — DateTimeOffset
│   └── RevokedAtUtc      — DateTimeOffset?
│
└── UsageTracking (AggregateRoot — extraction candidate for PolicyService)
    ├── TenantId          — inherited from Entity (RLS)
    ├── SubjectId         — SubjectId
    ├── Action            — string
    ├── ResourceType      — string
    ├── ResourceId        — string
    ├── Window            — TimeWindow (owned value object)
    ├── Count             — long
    ├── WindowStart       — DateTimeOffset
    ├── WindowEnd         — DateTimeOffset
    └── LastAccessedAt    — DateTimeOffset
```

### Aggregate Relationships

```
RoleAssignment  ──references──▶  Role  ──contains──▶  PermissionGrant  ──references──▶  Permission
                      │
                      └── DepartmentId (immutable external identity reference)
```

Role owns PermissionGrant. RoleAssignment references Role by RoleId. The Department is identified by DepartmentId on Role — AuthorizationService does not own or load Department.

### Removed Aggregates

- **Department** — class already deleted from source. Orphaned tables (`authz.Departments`, `authz.DepartmentMembers`) remain in initial migration and must be dropped.

---

## Invariants

### Role

| Invariant | Rule | Enforcement | Status |
|-----------|------|-------------|--------|
| DepartmentId required | Non-empty Guid on creation | `Role.Create()` factory | **MISSING** — no DepartmentId parameter |
| DepartmentId immutable | Never changes after creation | No setter exposed, no method to change | **MISSING** — property needs `private set` or `init` |
| Role name unique within Department | `(TenantId, DepartmentId, Name)` unique | DB unique index | **MISSING** — current index is `(TenantId, Name)` |
| Max 100 roles per tenant | Count check | `Role.cs:35-36` | Correct |
| Max hierarchy depth of 5 | Depth check | `Role.cs:39-40`, `Role.cs:71-72` | Correct |
| No self-inheritance | `parentRoleId != Id` | `Role.cs:67-68` | Correct |
| Role must be Active to assign | Status check | **MISSING** | Bug |
| Disabled Roles cannot be assigned | Status check | **MISSING** — Disabled status does not exist yet | New invariant |

### Permission

| Invariant | Rule | Enforcement | Status |
|-----------|------|-------------|--------|
| Key unique within tenant | `(TenantId, Key)` unique | DB unique index | Correct |
| Key format validated | Regex | `PermissionKey.Create()` | Correct |
| Global permissions prohibited | Must contain `.` | `PermissionKey.Create()` | Correct |
| Only Published permissions grantable | Lifecycle check | **MISSING** — no check on grant write path | Bug |
| Lifecycle transitions valid | State machine | `Permission.cs:52-118` | Correct |

### RoleAssignment

| Invariant | Rule | Enforcement | Status |
|-----------|------|-------------|--------|
| SubjectId required | Non-empty Guid | `SubjectId.From()` | Correct |
| RoleId required | Non-empty Guid | `RoleId.From()` | Correct |
| Tenant-scoped | Composite PK | `RoleAssignmentConfiguration.cs:13` | Correct |
| One active assignment per SubjectId | Business invariant | **NOT ENFORCED** | Bug |
| Role must be Active (not Inactive or Disabled) | Status check | **NOT ENFORCED** | Bug |
| DepartmentId NOT stored | Derived from Role | **Currently stores it** — to be removed | Fix |

### PermissionGrant

| Invariant | Rule | Enforcement | Status |
|-----------|------|-------------|--------|
| RoleId required | Non-empty Guid | `RoleId.From()` | Correct |
| PermissionId required | Non-empty Guid | `PermissionId.From()` | Correct |
| Tenant-scoped | Composite PK | `PermissionGrantConfiguration.cs:13` | Correct |
| One active grant per Role-Permission pair | Duplicate check | `GrantPermissionCommandHandler.cs:21` | Correct |
| Permission must be Published | Lifecycle check | **NOT ENFORCED** | Bug |

---

## Transaction Boundaries

| Operation | Scope | Consistency | Notes |
|-----------|-------|-------------|-------|
| CreateRole | Single aggregate (Role) | Strong | DepartmentId validated by TenantService integration before aggregate creation |
| AssignRole | Read Role, write RoleAssignment | Strong within service | Must enforce "one Role per User" invariant; must check Role is Active |
| GrantPermission | Read Role + Permission, write PermissionGrant | Strong within service | Must enforce "Published" lifecycle check |
| RevokeRole | Single aggregate (RoleAssignment) | Strong | |
| RevokePermission | Single aggregate (PermissionGrant) | Strong | |
| EvaluateDecision | Read-only | Eventual | **Must not call TenantService.** All data resolved locally. |

### Cross-Service Integration Boundaries

| Boundary | Write Operations | Read Operations (Authorization) |
|----------|-----------------|-------------------------------|
| TenantService → AuthorizationService | TenantService integration validates Department existence before CreateRole | **No synchronous calls.** DepartmentId resolved from local Role data. |
| IdentityService → AuthorizationService | SubjectId is an opaque reference; no validation needed at assignment time | SubjectId resolved from local RoleAssignment data. |
| AuthorizationService → TenantService | **Never during authorization evaluation.** Department data must be locally available. | N/A |

### Runtime Coupling Rule

Authorization decisions must NEVER synchronously call TenantService. Department validation is required ONLY for write operations (CreateRole, future operations). Permission evaluation, RBAC resolution, and authorization decisions execute entirely inside AuthorizationService without synchronous dependency on TenantService. If Department information is required during authorization, it must already exist inside AuthorizationService through local persistence, cache, or asynchronous synchronization. Authorization must remain operational even if TenantService is temporarily unavailable.

---

## Consistency Model

| Operation | Consistency | Justification |
|-----------|-------------|---------------|
| CreateRole | Strong | Single aggregate. DepartmentId validated by TenantService integration before creation. |
| AssignRole | Strong | Cross-aggregate read (Role) + write (RoleAssignment). One-Role-per-User invariant enforced within AuthorizationService. |
| GrantPermission | Strong | Cross-aggregate read (Role + Permission) + write (PermissionGrant). |
| RevokeRole | Strong | Single aggregate. |
| RevokePermission | Strong | Single aggregate. |
| EvaluateDecision | Eventual | Reads from cache + local data. **No TenantService calls.** DepartmentId resolved from Role at evaluation time. |

---

## Ownership Boundaries

| Concept | Owner | AuthorizationService Relationship |
|---------|-------|----------------------------------|
| Tenant | TenantService | TenantId inherited from Entity (RLS) |
| Department | TenantService | **Single source of truth.** DepartmentId on Role is an external identity reference. |
| User | IdentityService | SubjectId on RoleAssignment is an external identity reference |
| User ↔ Department | IdentityService | Not modeled in AuthorizationService |
| User ↔ Tenant | IdentityService | Not modeled in AuthorizationService |
| Role | AuthorizationService | **Owns.** Contains immutable DepartmentId as external reference. |
| Permission | AuthorizationService | **Owns.** |
| PermissionGrant | AuthorizationService | **Owns.** Links Role to Permission. |
| RoleAssignment | AuthorizationService | **Owns.** Links SubjectId to RoleId. |
| UsageTracking | PolicyService (future) | Currently owned by AuthorizationService. Extraction target. |
| Quotas / Rate Limiting | PolicyService (future) | Currently owned by AuthorizationService. Extraction target. |

### Department Identity Reference Pattern

AuthorizationService uses DepartmentId as an opaque identity reference on Role. This follows the same pattern as SubjectId on RoleAssignment (reference to User in IdentityService). The reference is:

- **Set at creation time** — `Role.Create(tenantId, departmentId, name, ...)`
- **Immutable** — no method to change DepartmentId after creation
- **Validated by TenantService integration** during write operations, not by the Role aggregate itself
- **Never loaded as an aggregate** — no `IDepartmentRepository` in AuthorizationService
- **Never queried from TenantService during authorization evaluation** — authorization must be self-contained
- **Used for queries** — "list Roles in Department X"
- **Used for uniqueness** — `(TenantId, DepartmentId, Name)` prevents cross-Department name collisions

### Department Lifecycle and Role Status

When a Department becomes inactive or is soft-deleted in TenantService, the following applies:

- All Roles with that DepartmentId transition to `Disabled` status
- Disabled Roles cannot be assigned to Users
- Disabled Roles do not participate in authorization decisions
- Disabled Roles remain persisted with their original DepartmentId for audit and historical consistency
- Roles are never automatically moved to another Department
- The Department→Role Disabled transition is triggered by TenantService publishing a Department status change event, which AuthorizationService consumes asynchronously

### PolicyService Responsibilities

PolicyService evaluates policies. It does NOT model organizational structure. Specifically:
- PolicyService does NOT define which Departments exist
- PolicyService does NOT manage the Department→Role hierarchy
- PolicyService does NOT validate Department existence
- PolicyService evaluates ABAC rules, OPA policies, quotas, and rate limits
- Authorization decisions may consume Department context that is locally available within AuthorizationService

---

## Codebase Findings

### Bugs

| ID | Location | Description | Priority |
|----|----------|-------------|----------|
| BUG-1 | `Role.cs:33` | `Role.Create()` has no `DepartmentId` parameter | P0 |
| BUG-2 | `RoleConfiguration.cs:14` | Unique index `(TenantId, Name)` allows cross-Department name collisions | P0 |
| BUG-3 | `RoleAssignment.cs:28` | Stores redundant `DepartmentId` | P0 |
| BUG-4 | Multiple | "User has exactly one Role" not enforced | P0 |
| BUG-5 | `RoleStatus.cs` | No `Disabled` status for Department-deactivated Roles | P0 |
| BUG-6 | `Role.cs` | DepartmentId is mutable (no immutability constraint) | P0 |
| BUG-7 | `RoleAssignment.cs` | Inactive/Disabled Roles can be assigned — no status check | P1 |
| BUG-8 | `PermissionGrant.cs:28` | Non-Published Permissions can be granted | P1 |
| BUG-9 | `IRoleAssignmentRepository.cs:12` | `GetSubjectIdsByRoleAsync` lacks `tenantId` — cross-tenant leak | P1 |
| BUG-10 | `OpaSyncConsumer.cs:17-26` | OPA sync event types PascalCase vs outbox kebab-case — events never match | P1 |

### Orphaned Database Artifacts

| Artifact | Location | Action |
|----------|----------|--------|
| `authz.Departments` table | `InitialCreate.cs:143-160` | Drop |
| `authz.DepartmentMembers` table | `InitialCreate.cs:198-216` | Drop |
| FK `DepartmentMembers.DepartmentId → Departments.Id` | `InitialCreate.cs:210-211` | Drop |
| Index `IX_Departments_TenantId_IsDeleted` | `InitialCreate.cs:263-266` | Drop |
| Index `IX_DepartmentMembers_SubjectId` | `InitialCreate.cs:257-260` | Drop |

### Architectural Smells

| ID | Location | Description |
|----|----------|-------------|
| SMELL-1 | `PermissionGrant.cs:9` | Inherits `AggregateRoot` but behaves as Entity |
| SMELL-2 | `IPermissionRepository.cs:14-17` | Repository manages both Permission and PermissionGrant |
| SMELL-3 | `UsageTracking.cs:9` | Non-standard PK, PascalCase table name |

### Dead Code

| ID | Location | Description |
|----|----------|-------------|
| DEAD-1 | `AuthorizationDecisionReason.cs:15` | `DelegationDenied` — never produced |
| DEAD-2 | `IUsageTrackingRepository.cs:12` | `GetExpiredAsync` — only PolicyService extraction candidate |
| DEAD-3 | `IAnalyticsEventSink.cs` | Analytics — cross-cutting concern, not authorization |

### Domain Events

**Published (via Outbox):**

| Event | Outbox Type | Trigger |
|-------|-------------|---------|
| `RoleCreatedDomainEvent` | `authorization.role-created.v1` | `CreateRoleCommandHandler` |
| `RoleActivatedDomainEvent` | `authorization.role-activated.v1` | `Role.Activate()` |
| `RoleDeactivatedDomainEvent` | `authorization.role-deactivated.v1` | `Role.Deactivate()` |
| `RoleParentChangedDomainEvent` | `authorization.role-parent-changed.v1` | `Role.SetParent()` |
| `RoleAssignedDomainEvent` | `authorization.role-assigned.v1` | `AssignRoleCommandHandler` |
| `RoleRevokedDomainEvent` | `authorization.role-revoked.v1` | `RevokeRoleCommandHandler` |
| `PermissionCreatedDomainEvent` | `authorization.permission-created.v1` | `CreatePermissionCommandHandler` |
| `PermissionGrantedDomainEvent` | `authorization.permission-granted.v1` | `GrantPermissionCommandHandler` |
| `PermissionRevokedDomainEvent` | `authorization.permission-revoked.v1` | `RevokePermissionCommandHandler` |
| `UsageTrackingCreatedDomainEvent` | `authorization.usage-tracking-created.v1` | `UsageTracking.Create()` |
| `UsageIncrementedDomainEvent` | `authorization.usage-incremented.v1` | `UsageTracking.Increment()` |

**Dead events (published, never consumed):**

`PermissionSubmittedForReviewDomainEvent`, `PermissionApprovedDomainEvent`, `PermissionPublishedDomainEvent`, `PermissionDeprecatedDomainEvent`, `PermissionArchivedDomainEvent`, `PermissionVersionCreatedDomainEvent`, `AuthorizationEvaluatedDomainEvent`

**Consumed (via MassTransit):**

| Event Type | Source | Consumer Action |
|-----------|--------|-----------------|
| `UserRegisteredV1`, `UserActivatedV1`, `UserLockedV1`, etc. | IdentityService | Cache invalidation for subject |
| `authorization.role-assigned.v1` | Self (outbox) | Cache invalidation for subject |
| `authorization.role-revoked.v1` | Self (outbox) | Cache invalidation for subject |
| `authorization.permission-granted.v1` | Self (outbox) | Cache invalidation for all subjects with that role |
| `authorization.permission-revoked.v1` | Self (outbox) | Cache invalidation for all subjects with that role |

---

## Migration Strategy

### Phase 1 — Critical Domain Fixes

**Goal:** Make the Domain Model match the business domain.

1. **Add DepartmentId to Role aggregate (immutable)**
   - Add `Guid DepartmentId` property with no public setter
   - Add `departmentId` parameter to `Role.Create()` factory
   - Add `DepartmentId` to `RoleCreatedDomainEvent`
   - No method to change DepartmentId — immutability enforced by absence of setter
   - Migration: add `DepartmentId` column to `authorization_roles`

2. **Change Role unique index to `(TenantId, DepartmentId, Name)`**
   - `RoleConfiguration.cs` — rebuild unique index
   - `RoleRepository` — `GetByNameAsync` and `ExistsByNameAsync` accept DepartmentId

3. **Add TenantService integration for CreateRole**
   - `CreateRoleCommand` includes DepartmentId
   - `CreateRoleCommandHandler` validates Department via TenantService integration before calling `Role.Create()`
   - Validation is write-path only — never called during authorization evaluation

4. **Extend RoleStatus with Disabled**
   - Add `Disabled = 3` to `RoleStatus` enum
   - Add `Disable()` method to Role aggregate (triggered by Department lifecycle event)
   - Disabled Roles cannot be assigned — enforce in `AssignRoleCommandHandler`

5. **Remove DepartmentId from RoleAssignment**
   - Remove `departmentId` from `RoleAssignment.Assign()`
   - Remove `DepartmentId` from `RoleAssignedDomainEvent`
   - Remove `DepartmentId` from `AssignRoleRequest`
   - Update `OpaPolicyEvaluationGateway` — resolve DepartmentId by loading Role

6. **Enforce "One User has exactly one Role"**
   - Application layer: check existing active assignment in `AssignRoleCommandHandler`
   - Database: partial unique index on `(TenantId, SubjectId)` where `RevokedAtUtc IS NULL`

7. **Add Active Role check in assignment path**
   - Reject assignment if Role status is not Active

8. **Drop orphaned Department tables**
   - Migration: `DROP TABLE authz.DepartmentMembers; DROP TABLE authz.Departments;`

9. **Fix cross-tenant leak in GetSubjectIdsByRoleAsync**
   - Add `tenantId` parameter

10. **Fix OPA sync event type mismatch**
    - Align event type strings with outbox kebab-case format

### Phase 2 — Architectural Cleanup

1. **Simplify Permission lifecycle** — reduce to Active/Inactive
2. **Downgrade PermissionGrant to Entity** — move into Role aggregate
3. **Remove dead code** — 7 dead events, `DelegationDenied`, `IAnalyticsEventSink`

### Phase 3 — PolicyService Extraction

All usage/quota/rate-limiting components extracted when PolicyService is designed.

---

## Consequences

### Positive

- Domain Model accurately represents "Department contains Roles"
- Role uniqueness is department-scoped
- DepartmentId on Role is immutable — no accidental reassignment
- Redundant DepartmentId removed from RoleAssignment
- "One User has exactly one Role" enforced via application + database
- Disabled status handles Department lifecycle consequences
- TenantService integration validates Department on write path
- Authorization evaluation has zero TenantService coupling
- Orphaned Department tables dropped

### Negative

- Breaking change: `CreateRoleRequest` now requires DepartmentId
- Breaking change: `AssignRoleRequest` no longer accepts DepartmentId
- Breaking change: `RoleAssignedDomainEvent` no longer carries DepartmentId
- OPA policies referencing `department_id` from RoleAssignment must be updated
- Database migration required for Role index rebuild, DepartmentId column, Disabled status
- TenantService integration adds a synchronous call on the write path (CreateRole only)

### Risks

- OPA policies consuming `department_id` from RoleAssignment event must be rewritten
- Department lifecycle event handling must be reliable — stale Disabled transitions could leave Roles in incorrect state
- TenantService unavailability blocks CreateRole but does NOT block authorization evaluation (by design)

---

## Implementation Roadmap

### Phase 1 — Critical Domain Fixes

| Priority | Task | Evidence | Effort |
|----------|------|----------|--------|
| P0 | Add `DepartmentId` (immutable) to Role | `Role.cs:17,33`, `CreateRoleCommand.cs` | 1 day |
| P0 | Change Role unique index to `(TenantId, DepartmentId, Name)` | `RoleConfiguration.cs:14` | 0.5 day |
| P0 | Add TenantService integration for CreateRole | `CreateRoleCommandHandler.cs` | 1 day |
| P0 | Add `Disabled` to RoleStatus | `RoleStatus.cs` | 0.5 day |
| P0 | Remove `DepartmentId` from RoleAssignment | `RoleAssignment.cs:28`, `AssignRoleRequest.cs:3` | 0.5 day |
| P0 | Enforce "One User has one Role" | `AssignRoleCommandHandler.cs`, `RoleAssignmentConfiguration.cs` | 1 day |
| P1 | Add Active/Disabled Role check in assignment path | `AssignRoleCommandHandler.cs` | 0.5 day |
| P1 | Add Published Permission check in grant path | `PermissionGrant.Grant()` or handler | 0.5 day |
| P1 | Fix `GetSubjectIdsByRoleAsync` cross-tenant leak | `IRoleAssignmentRepository.cs:12` | 0.5 day |
| P1 | Fix OPA sync event type mismatch | `OpaSyncConsumer.cs:17-26` | 0.5 day |
| P2 | Drop orphaned `authz.Departments` and `authz.DepartmentMembers` | `InitialCreate.cs:143-216` | 0.5 day |

**Total Phase 1: 6.5 days**

### Phase 2 — Architectural Cleanup

| Priority | Task | Evidence | Effort |
|----------|------|----------|--------|
| P2 | Simplify Permission lifecycle | `Permission.cs:9-17` | 1 day |
| P2 | Downgrade PermissionGrant to Entity | `PermissionGrant.cs:9` | 1 day |
| P2 | Remove dead code | 7 dead events, `DelegationDenied`, `IAnalyticsEventSink` | 0.5 day |

**Total Phase 2: 2.5 days**

### Phase 3 — PolicyService Extraction (Identify Only)

| Component | Current Location | Extraction Trigger |
|-----------|-----------------|-------------------|
| UsageTracking aggregate | `Aggregates/UsageTracking/` | When PolicyService is designed |
| UsageAccountingService | `Services/UsageAccountingService.cs` | When PolicyService is designed |
| IRealTimeUsageStore | `Services/IRealTimeUsageStore.cs` | When PolicyService is designed |
| RedisRealTimeUsageStore | `Infrastructure/Caching/RedisRealTimeUsageStore.cs` | When PolicyService is designed |
| QuotaConsumptionCalculator | `Services/QuotaConsumptionCalculator.cs` | When PolicyService is designed |
| QuotaSnapshot VO | `ValueObjects/QuotaSnapshot.cs` | When PolicyService is designed |
| UsageCounter VO | `ValueObjects/UsageCounter.cs` | When PolicyService is designed |
| TimeWindow VO | `ValueObjects/UsageCounter.cs:54-127` | When PolicyService is designed |
| IUsageTrackingRepository | `Repositories/IUsageTrackingRepository.cs` | When PolicyService is designed |
| MonthlyQuotaResetJob | `Infrastructure/Scheduling/MonthlyQuotaResetJob.cs` | When PolicyService is designed |
| RecordOperationResult command | `Features/RecordOperationResult/` | When PolicyService is designed |
| Delegation resolution (Step 5) | `AuthorizationDecisionService.cs:93-103` | When PolicyService is designed |
| Usage check (Step 3) | `AuthorizationDecisionService.cs:34-83` | When PolicyService is designed |

---

## Final Architectural Revisions

| # | Change | Rationale |
|---|--------|-----------|
| 1 | **DepartmentId is now immutable on Role.** Added as a stated invariant: no public setter, no method to change DepartmentId after creation. Moving a Role between Departments is not a supported use case. | Decision 9. DepartmentId is a stable identity reference, not a mutable attribute. Immutability prevents accidental reassignment and preserves historical consistency. |
| 2 | **RoleStatus gains Disabled state.** When a Department becomes inactive or is soft-deleted, all its Roles transition to Disabled. Disabled Roles cannot be assigned and do not participate in authorization decisions. | Decision 10. The Department lifecycle has direct consequences on Role usability. The Domain Model must represent this. Disabled is semantically distinct from Inactive (administrator action vs. Department lifecycle consequence). |
| 3 | **Department validation via TenantService integration.** CreateRole validates Department existence through TenantService before aggregate creation. The Role aggregate itself never validates DepartmentId. | Decision 11. Validation is an integration concern, not an aggregate responsibility. The aggregate assumes a valid DepartmentId because the integration workflow guarantees it. |
| 4 | **Runtime Coupling Rule added.** Authorization decisions must never synchronously call TenantService. Department validation applies ONLY to write operations. Authorization evaluation is fully self-contained. | Runtime Coupling Rule. Authorization must remain operational if TenantService is unavailable. Department data needed during evaluation must be locally available. |
| 5 | **PolicyService responsibilities explicitly bounded.** PolicyService evaluates policies. It does NOT model organizational structure, manage Department→Role hierarchy, or validate Department existence. | Decision 8 + Architecture Principle 9. Organizational hierarchy belongs to the Domain Model. PolicyService is a policy evaluation engine, not an organizational structure manager. |
| 6 | **Architecture Principles section added.** Ten explicit principles govern all decisions in this ADR. | Ensures consistency and provides a reference for future decisions. Prevents regression to previously rejected approaches. |
| 7 | **Consistency Model updated.** CreateRole now notes TenantService integration. EvaluateDecision explicitly states no TenantService calls. | Reflects the Runtime Coupling Rule and Decision 11. Write-path validation is distinguished from read-path evaluation. |
| 8 | **Cross-Service Integration Boundaries section added.** Separates write operations (with TenantService integration) from authorization evaluation (fully self-contained). | Makes the coupling boundary explicit. Prevents future code from introducing synchronous TenantService calls in authorization paths. |
| 9 | **Department Lifecycle and Role Status section added.** Documents the Department→Disabled Role transition, including async event handling and historical consistency. | Decision 10. Provides the complete specification for how Department lifecycle affects Roles. |
| 10 | **"Department Identity Reference Pattern" section updated.** Added immutability constraint, TenantService validation note, and no-TenantService-during-authorization rule. | Consistency with Decisions 9, 11, and the Runtime Coupling Rule. Previous version incorrectly stated "never validated against TenantService." |
| 11 | **Invariants table updated.** Added DepartmentId immutability, Disabled status invariants, and Active+Disabled check for assignment. | Reflects new invariants from Decisions 9 and 10. |
| 12 | **Bugs table updated.** Added BUG-5 (missing Disabled status) and BUG-6 (DepartmentId mutable). Renumbered existing entries. | New findings from Decisions 9 and 10. |
| 13 | **Phase 1 effort updated to 6.5 days.** Added TenantService integration (1 day) and Disabled status (0.5 day). | Reflects additional implementation scope from Decisions 10 and 11. |
| 14 | **Risks section updated.** Added Department lifecycle event reliability risk and TenantService unavailability impact scope. | Addresses operational risks from Decisions 10, 11, and the Runtime Coupling Rule. |


The AuthorizationService trusts department_id contained in the authenticated JWT issued by IdentityService. The JWT represents the authoritative identity snapshot at the time of issuance. Department validation on write operations is performed against this trusted claim, avoiding synchronous calls to IdentityService. Changes in department membership become effective after token refresh or re-authentication.