# ADR-014

Title:

Department Membership Ownership — Authoritative Lifecycle and Service Boundaries

Status:

Proposed

---

# Context

Department Membership determines which department a user operates within. Today, `DepartmentId` is persisted on the `User` aggregate in IdentityService, written into the JWT at login, and parsed independently by each service's `HttpRequestContextAccessor`. AuthorizationService also maintains `DepartmentId` on the `Role` aggregate, but no synchronization exists between the two sources.

The business rules established below require that Department Membership be derived from the assigned Role, not stored directly on the User. This ADR documents the discovery findings, identifies every assumption in ADR-012 and ADR-013 that becomes invalid, and defines the target architecture.

---

# 1. Business Rules (Authoritative)

**Organization:**
- A Tenant owns many Departments.
- A Department owns many Roles.
- Every Role belongs to exactly one Department and one Tenant.

**Identity:**
- A User belongs to exactly one Tenant.
- A User can have exactly one Role.
- A User can never hold multiple Roles simultaneously.
- A User is NOT directly assigned to a Department.
- Department Membership is ALWAYS derived from the assigned Role.

**Ownership:**
- TenantService owns: Department definition, lifecycle, metadata, hierarchy.
- AuthorizationService owns: Role, Permission, Grants, Role Assignment — and therefore Department Membership through Role Assignment.
- IdentityService owns: User identity, credentials, authentication, lifecycle. IdentityService does NOT own Department Membership.

---

# 2. Discovery Findings

## 2.1 Does User.DepartmentId still exist?

**Yes.** `User.DepartmentId` is declared at `User.cs:29` as `public Guid DepartmentId { get; private set; }`.

**Who reads it:**
- `LoginCommandHandler.cs:126` — copies into `Session.Create(tenantId, user.Id, user.DepartmentId, ...)`
- `HmacJwtTokenGenerator.cs:61` — writes `user.DepartmentId` into JWT as `department_id`
- `ChangeDepartmentCommandHandler.cs:43` — reads previous value before mutation
- `GetUserQueryHandler.cs:22` — includes in response DTO
- `CachedUserRepository.cs:196` — maps into cache DTO
- `IdentityDbContext.cs:67,71` — EF index and property configuration

**Who writes it:**
- `User.Register` (`User.cs:47`) — set at registration from `request.DepartmentId`
- `User.ChangeDepartment` (`User.cs:269`) — mutable via explicit command

**Is it authoritative?** Under the current implementation, yes — it is the sole source that flows into JWT and RequestContext. Under the business rules defined in this ADR, no — it is redundant and must be removed.

**Conclusion:** `User.DepartmentId` is a legacy artifact. It should be removed once Department Membership is derived from Role.

## 2.2 Complete DepartmentId Lifecycle Trace

| Stage | Source | File | Evidence |
|-------|--------|------|----------|
| Department creation | `Department.Create()` | TenantService | TenantService owns definition |
| Role creation | `Role.Create(tenantId, departmentId, ...)` | AuthorizationService | `Role.cs:35` — immutable `DepartmentId` |
| Role assignment | `AssignRoleCommandHandler` | AuthorizationService | `AssignRoleCommandHandler.cs:26` — validates `Context.DepartmentId == role.DepartmentId` |
| User registration | `User.Register(tenantId, request.DepartmentId, ...)` | IdentityService | `RegisterCommandHandler.cs:74` — user-level copy |
| Session creation | `Session.Create(tenantId, user.Id, user.DepartmentId, ...)` | IdentityService | `LoginCommandHandler.cs:126` — copies from User |
| JWT issuance | `jsonWriter.WriteString("department_id", user.DepartmentId)` | IdentityService | `HmacJwtTokenGenerator.cs:61` — from User |
| CurrentPrincipalFactory | **Never parses `department_id`** | Platform | `CurrentPrincipalFactory.cs` — no DepartmentId field |
| HttpRequestContextAccessor | `FindFirst("department_id")` | All 3 services | Triple independent parsing |
| RLS enforcement | `TenantRlsInterceptor.cs:86-87` | Platform | Reads `ctx.DepartmentId` |
| IdentityDbContext filter | Expression tree on `IDepartmentBound.DepartmentId` | IdentityService | `IdentityDbContext.cs:162-163` |
| Authorization decision | `AssignRoleCommandHandler.cs:26` | AuthorizationService | Compares `Context.DepartmentId` with `role.DepartmentId` |

**Authoritative source at authentication time:** `User.DepartmentId` (IdentityService).
**Authoritative source for authorization:** `Role.DepartmentId` (AuthorizationService).
**Gap:** These two values are never synchronized. The `AssignRole` handler validates they match at assignment time, but nothing enforces consistency afterward.

## 2.3 Should User.DepartmentId exist?

**No.** Under the business rules, a User is not directly assigned to a Department. Department Membership is derived from the assigned Role. `User.DepartmentId` is a denormalized copy that creates a dual-source problem.

## 2.4 Should ChangeDepartment exist?

**No.** If Department Membership derives from Role, then changing a user's department means changing their assigned Role. `ChangeUserRole` already handles this: it revokes the current assignment and creates a new one pointing to a Role in the target department. `ChangeDepartment` is an independent path that bypasses the AuthorizationService entirely, creating an inconsistency vector.

## 2.5 Synchronization between User.DepartmentId and Role.DepartmentId

**None exists.** Evidence:
- `AssignRoleCommandHandler` validates `Context.DepartmentId == role.DepartmentId` at assignment time (line 26), but does NOT update `User.DepartmentId`.
- `ChangeUserRoleCommandHandler` validates both the caller's department and that source/target Roles share the same department (line 44), but does NOT update `User.DepartmentId`.
- IdentityService has no event consumer for `RoleAssignedDomainEvent` or `authorization.role-assigned.v1`. Grep for `RoleAssigned` in IdentityService returns zero matches.
- AuthorizationService's `CacheInvalidationConsumer` handles `UserDepartmentChangedV1` but only invalidates the auth cache — it does NOT update any Role.

**Risk:** After `AssignRole` or `ChangeUserRole`, `User.DepartmentId` and `Role.DepartmentId` can diverge. The JWT carries `User.DepartmentId`, but the authorization pipeline compares against `Role.DepartmentId`. A user assigned to a Role in Department B while `User.DepartmentId` still points to Department A would pass RLS (Department A) but fail authorization checks expecting Department B.

## 2.6 Login Pipeline Validation

Today: JWT obtains `department_id` from `user.DepartmentId` (`HmacJwtTokenGenerator.cs:61`).

Per business rules: `department_id` originates from Authorization (derived from Role), not Identity. The login pipeline must resolve the user's assigned Role via AuthorizationService, then obtain `department_id` from that Role.

**Required change:** `LoginCommandHandler` must call AuthorizationService to resolve the user's active Role, extract `Role.DepartmentId`, and pass it to `tokenGenerator.GenerateAccessToken`. The JWT `department_id` claim must originate from Authorization, not User.

## 2.7 CurrentPrincipal Validation

Today: `ICurrentPrincipal` has no `DepartmentId` property. Each accessor parses `FindFirst("department_id")` independently.

Per business rules: `DepartmentId` inside `CurrentPrincipal` must come from JWT only (which itself originates from Authorization via Role). `CurrentPrincipalFactory` must parse the `department_id` claim once. The accessor must read `principal.DepartmentId`, never touch JWT claims directly.

## 2.8 ADR-012 Assumptions That Become Invalid

| ADR-012 Section | Assumption | Impact |
|-----------------|------------|--------|
| Section 3 (Target Architecture) | `RequestContext` includes `DepartmentId` as a field sourced from the accessor | Field remains, but source changes from User to Role (via JWT) |
| Phase 1 (TenantSource enum) | `TenantSource` is introduced into Platform | `TenantSource` should include `Role` as a source for `DepartmentId` |
| Phase 5 (Single HttpRequestContextAccessor) | Accessor reads `HttpContext.Items` and projects into record | Accessor must also read `principal.DepartmentId` — already planned, no change |
| Section 6 (Architectural Rules) | "Execution context is infrastructure state, not business data" | `DepartmentId` is business data derived from Role; the rule still holds because it flows through infrastructure (JWT → principal → context) |
| Detailed Plan §3 (Consumers) | `IdentityDbContext` expression trees reference `RequestContext.DepartmentId` | These trees must continue working after `DepartmentId` source changes to Role |

## 2.9 ADR-013 Assumptions That Become Invalid

| ADR-013 Section | Assumption | Impact |
|-----------------|------------|--------|
| §5 (Allowed Resolution Points) | `LoginCommandHandler` resolves `TenantId` via `TenantServiceClient` | Login must additionally resolve `DepartmentId` via AuthorizationService |
| §5 | `HmacJwtTokenGenerator` embeds `tenant_id = user.TenantId` | Generator must also embed `department_id` from resolved Role |
| §10 Rule 5 | "AuthorizationService never resolves TenantId" | Unchanged — but AuthorizationService becomes the source of `department_id` |
| §12 (Migration Impact) | "The slug→id cache and TenantService login-time lookup stay in IdentityService" | Login now also requires an AuthorizationService call to resolve Role |

---

# 3. Target Architecture

## 3.1 Entity Ownership Diagram

```
TenantService                    AuthorizationService              IdentityService
─────────────                    ────────────────────              ───────────────
Department                        Role                              User
├── DepartmentId (PK)             ├── RoleId (PK)                   ├── UserId (PK)
├── TenantId (FK)                 ├── TenantId (FK)                 ├── TenantId (FK)
├── Name                          ├── DepartmentId (FK) ──────────► │   (NO DepartmentId)
├── Status                        ├── Name                          ├── Username
└── ...                           ├── Status                        ├── PasswordHash
                                  └── ...                           └── ...
                                        │
                                        │ 1:1 constraint
                                        ▼
                                  RoleAssignment
                                  ├── SubjectId (= UserId)
                                  ├── RoleId (FK)
                                  └── AssignedBy
```

**Key:** `User` has NO `DepartmentId`. `RoleAssignment` connects User to Role. Role connects to Department.

## 3.2 Department Membership Derivation

```
User ──── RoleAssignment ──── Role ──── Department
                  │
                  └──► DepartmentId (derived, never stored on User)
```

To resolve a user's department:
1. Look up `RoleAssignment` for the user (AuthorizationService)
2. Load the assigned `Role` (AuthorizationService)
3. Read `Role.DepartmentId`
4. That IS the user's department

## 3.3 Authentication Flow

```
LoginRequest
    │
    ▼
LoginCommandHandler [IdentityService]
    │
    ├── Authenticate (password check, MFA)
    │
    ├── Resolve TenantId (TenantServiceClient.ResolveTenantIdBySlugAsync)
    │
    ├── Resolve Role ──────────────────────► AuthorizationServiceClient
    │   (get active RoleAssignment + Role)       .GetActiveRoleForUserAsync(userId)
    │
    ├── Obtain DepartmentId from Role.DepartmentId
    │
    └── Issue JWT
            ├── tenant_id  (from TenantService resolution)
            ├── sub        (from User.Id)
            ├── department_id  (from Role.DepartmentId)  ◄── CHANGED
            ├── role_id    (from RoleAssignment.RoleId)   ◄── NEW
            └── ...
```

## 3.4 JWT Ownership

| Claim | Origin | Owner |
|-------|--------|-------|
| `tenant_id` | TenantService (slug→id resolution) | IdentityService orchestrates |
| `sub` | User.Id | IdentityService |
| `department_id` | Role.DepartmentId | AuthorizationService |
| `role_id` | RoleAssignment.RoleId | AuthorizationService |
| `principal_type` | User type | IdentityService |

## 3.5 DepartmentId Ownership Through Request Lifecycle

```
JWT (post-auth)
    │
    ▼
CurrentPrincipalFactory [Platform]
    ├── Parses tenant_id  → ICurrentPrincipal.TenantId
    ├── Parses sub        → ICurrentPrincipal.UserId
    ├── Parses department_id → ICurrentPrincipal.DepartmentId  ◄── NEW
    └── Parses role_id    → ICurrentPrincipal.RoleId           ◄── NEW
    │
    ▼
TenantMiddleware [Platform]
    └── HttpContext.Items["TenantId"] = principal.TenantId
    │
    ▼
HttpRequestContextAccessor [Platform/Service]
    └── RequestContext = new(
            TenantId: principal.TenantId,
            CorrelationId: ...,
            RequestId: ...,
            UserId: principal.UserId,
            DepartmentId: principal.DepartmentId,    ◄── from principal, NOT JWT claims
            CanBypassTenantIsolation: ...)
    │
    ▼
Consumers (read-only):
    ├── TenantRlsInterceptor  (SET LOCAL app.current_department_id)
    ├── IdentityDbContext     (RLS filter on IDepartmentBound)
    ├── AssignRole validation (Context.DepartmentId == role.DepartmentId)
    └── OPA policy evaluation
```

---

# 4. Decision

| Question | Decision | Rationale |
|----------|----------|-----------|
| Who owns Department Definition? | **TenantService** | TenantService manages the Department aggregate lifecycle |
| Who owns Department Membership? | **AuthorizationService** | Through Role and RoleAssignment; every Role maps to exactly one Department |
| Should Identity persist DepartmentId? | **No.** Remove `User.DepartmentId` | Department Membership is derived from Role, not stored on User |
| Should Authorization derive Department Membership from Role? | **Yes.** This is the single source of truth | `Role.DepartmentId` is immutable and authoritative |
| Should `department_id` in JWT originate from Authorization? | **Yes.** Login resolves the user's Role via AuthorizationService, then reads `Role.DepartmentId` | Eliminates the dual-source problem |
| Should CurrentPrincipal treat DepartmentId as a projection from JWT only? | **Yes.** `CurrentPrincipalFactory` parses the `department_id` claim once; accessor reads from principal | Single parsing point, consistent with `TenantId` and `UserId` |

---

# 5. Migration Impact

Every service listed below must change after this ADR is approved.

## IdentityService

| Change | Scope | Risk |
|--------|-------|------|
| Remove `User.DepartmentId` property | Domain, Persistence, Caching | High — touches aggregate, DB schema, cache DTO |
| Remove `User.ChangeDepartment` method | Domain | Medium — removes a use case entirely |
| Delete `ChangeDepartmentCommandHandler` and `ChangeDepartmentCommand` | Application | Medium — removes an endpoint |
| Delete `ChangeDepartment` endpoint from `AuthEndpoints` | Api | Low — REST endpoint removal |
| Remove `Session.DepartmentId` | Domain, Persistence | Medium — Session no longer needs DepartmentId |
| Update `LoginCommandHandler` to resolve Role via AuthorizationService | Application | High — adds cross-service call at login |
| Update `HmacJwtTokenGenerator.GenerateAccessToken` to accept `departmentId` and `roleId` as parameters | Infrastructure | Medium — signature change |
| Remove `department_id` from `User.Register` flow | Application, Api | Medium — registration no longer requires DepartmentId |
| Update `RegisterCommandHandler` | Application | Medium — DepartmentId validation moves to AuthorizationService |
| Update `CachedUserRepository` DTOs | Infrastructure | Low — remove DepartmentId from cache shape |
| Update `GetUserQueryHandler` response | Application | Low — DepartmentId comes from Role, not User |
| Add event consumer for `authorization.role-assigned.v1` | Infrastructure | Low — for cache invalidation only |

## AuthorizationService

| Change | Scope | Risk |
|--------|-------|------|
| Add `GetActiveRoleForUserAsync(userId)` to `IRoleAssignmentRepository` | Domain, Infrastructure | Medium — new query |
| Create `AuthorizationServiceClient.GetDepartmentIdForUserAsync(userId)` | Infrastructure | Medium — new API endpoint |
| Expose `department_id` and `role_id` via new endpoint or enrich existing one | Api | Low |
| Remove `AssignRole` cross-check `Context.DepartmentId == role.DepartmentId` | Application | Low — no longer needed since user has no DepartmentId |
| Remove `ChangeUserRole` cross-check `command.Context.DepartmentId != targetRole.DepartmentId` | Application | Low — same reason |

## TenantService

| Change | Scope | Risk |
|--------|-------|------|
| Remove `DepartmentId` from `EndpointContext.From` if present | Api | Low — already not present |
| No domain changes | — | — |

## Platform

| Change | Scope | Risk |
|--------|-------|------|
| Add `Guid? DepartmentId` to `ICurrentPrincipal` | Abstractions | Low — additive |
| Add `Guid? DepartmentId` to `CurrentPrincipal` | Abstractions | Low — additive |
| Parse `department_id` in `CurrentPrincipalFactory` | Infrastructure | Low — additive |
| Add `Guid? RoleId` to `ICurrentPrincipal` (optional, for future use) | Abstractions | Low — additive |
| Remove `UserName` and `Roles` from `Platform.Abstractions.Tenant.RequestContext` | Abstractions | Low — dead fields |
| All `HttpRequestContextAccessor` implementations read `DepartmentId` from principal | Infrastructure | Low — already planned in ADR-012 |
| `TenantRlsInterceptor` — no change needed, reads `ctx.DepartmentId` | Infrastructure | None |

## Database Migrations

| Service | Migration | Risk |
|---------|-----------|------|
| IdentityService | Drop `DepartmentId` column from `users` table | High — requires data backfill or confirmation no consumers remain |
| IdentityService | Drop `DepartmentId` column from `sessions` table | Medium |
| AuthorizationService | No schema change | None |
| TenantService | No schema change | None |

---

# 6. Sequencing Notes

This ADR must be approved and its migration executed BEFORE ADR-012 Phase 5 (single accessor consolidation) and BEFORE ADR-013 dead-code cleanup. The reason: the login pipeline change (resolving DepartmentId from Role) affects what the JWT contains, which affects what `CurrentPrincipalFactory` parses, which affects what the accessor projects. If the accessor consolidation happens first, it would need to be reworked.

Recommended sequence:
1. Approve ADR-014
2. Add `department_id` and `role_id` to `ICurrentPrincipal` / `CurrentPrincipalFactory` (Platform)
3. Add `GetActiveRoleForUserAsync` to AuthorizationService
4. Update `LoginCommandHandler` to resolve Role and pass `departmentId` to JWT generator
5. Remove `User.DepartmentId` and `Session.DepartmentId` (IdentityService)
6. Remove `ChangeDepartment` use case (IdentityService)
7. Clean up `AssignRole` / `ChangeUserRole` department cross-checks (AuthorizationService)
8. Continue ADR-012 / ADR-013 refactoring
