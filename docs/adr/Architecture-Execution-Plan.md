# Architecture Execution Plan

Status: Ready for Review

---

# PHASE 1 — ADR Consistency Review

## ADR-012 (RequestContext Platform Ownership) — Status: Partially Implemented

**Still valid?** Yes, with one correction.

**What is done (TenantService only):**
- TenantDbContext, TenantDbContextFactory: migrated to Platform accessor
- 6 TenantService handlers: migrated to Platform accessor
- TenantService.EndpointContext: rewritten with named args to Platform record
- TenantService.HttpRequestContextAccessor: rewritten (single Platform interface)

**What is broken:**
- TenantService.ServiceCollectionExtensions.cs still registers the old local interface and performs a cast `(Platform.Abstractions.Tenant.IRequestContextAccessor)sp.GetRequiredService<TenantService.Application.Common.Abstractions.IRequestContextAccessor>()`. The accessor no longer implements the local interface, so this cast will throw `InvalidCastException` at runtime.

**What is NOT done:**
- IdentityService: 12 handlers, accessor, DI, local types — all untouched
- AuthorizationService: 12 command records embedding RequestContext, handlers, accessor, DI, local types — all untouched
- TenantService: local types not deleted, Tenancy/ not deleted, DI not fixed

**Section 3 (Target Architecture) correction:** The ADR lists `UserName`, `Roles`, `IpAddress`, `UserAgent`, `TenantSource` as fields on the canonical RequestContext. ADR-014 supersedes: `UserName` and `Roles` are dead (never populated, never consumed). `IpAddress` and `UserAgent` are consumed only by `LoginCommandHandler` for Session creation — these belong on Session, not on RequestContext. `TenantSource` is consumed only by dead Tenancy providers.

**Obsolete sections:**
- Phase 1 (Introduce Platform.TenantSource): `TenantSource` is only used by the dead Tenancy providers being deleted per ADR-013. Skip this phase entirely.
- Section 5 (ITenantContext verification): Already identified as orphaned; deletion is trivial.

## ADR-013 (TenantId Ownership) — Status: Accepted, Not Implemented

**Still valid?** Yes, completely.

**What is NOT done:**
- The dead `ITenantResolver`/`ITenantResolutionProvider`/`HeaderTenantProvider`/`HostTenantProvider` family in TenantService.Api.Tenancy/ still exists (7 files)
- TenantService.ServiceCollectionExtensions still registers these dead providers (lines 79-81)
- TenantService.ServiceCollectionExtensions still binds TenantHostOptions (lines 31-32)

**No conflicts with ADR-012 or ADR-014.** ADR-013's rules about propagation-only accessors are already satisfied by the rewritten TenantService accessor.

## ADR-014 (Department Membership) — Status: Proposed, Not Implemented

**Still valid?** Yes, completely. It supersedes certain assumptions in both ADR-012 and ADR-013.

**What supersedes ADR-012:**
- ADR-012 Phase 1 proposed introducing `Platform.Abstractions.Tenant.TenantSource`. ADR-014 eliminates the need: the dead Tenancy providers (only consumers of TenantSource) are deleted per ADR-013. **Skip ADR-012 Phase 1.**
- ADR-012 lists `UserName` and `Roles` on the canonical RequestContext. ADR-014 discovery confirms these are dead fields. **Remove them.**
- ADR-012 lists `IpAddress`, `UserAgent` on the canonical RequestContext. These are consumed only by `Session.Create` in LoginCommandHandler. They belong in Session, not RequestContext. **Remove from RequestContext; pass directly to Session.Create.**

**What supersedes ADR-013:**
- ADR-013 §5 lists JWT issuance as "writes `tenant_id = user.TenantId`". ADR-014 adds: login must also resolve `department_id` from Role via AuthorizationService. **ADR-013 §5 must be updated to reflect the additional AuthorizationService call.**

---

# PHASE 2 — Cross-ADR Dependency Graph

```
ADR-014 (Department Membership)
│
├─► P1: Platform principal enrichment (ICurrentPrincipal.DepartmentId, CurrentPrincipalFactory)
│     │
│     ├─► P2: AuthorizationService new endpoint (GetActiveRoleForUser)
│     │     │
│     │     └─► P3: Login pipeline (resolve Role → JWT department_id)
│     │           │
│     │           └─► P4: Remove User.DepartmentId, Session.DepartmentId
│     │                 │
│     │                 └─► P5: Remove ChangeDepartment use case
│     │
│     └─► P6: All HttpRequestContextAccessor implementations read DepartmentId from principal
│
ADR-012 (RequestContext Consolidation)
│
├─► P7: Fix TenantService DI (broken cast, remove dual registration)
│     │
│     └─► P8: Delete TenantService local types (IRequestContextAccessor, RequestContext, TenantSource)
│
├─► P9: AuthorizationService Phase 0 (remove RequestContext from 12 command records)
│     │
│     └─► P10: Migrate AuthorizationService handlers to Platform accessor
│
├─► P11: Migrate IdentityService handlers (12) to Platform accessor
│
├─► P12: Delete all local IRequestContext/RequestContext/TenantSource across all services
│
└─► P13: Single Platform HttpRequestContextAccessor + AddPlatformRequestContext()

ADR-013 (TenantId Cleanup)
│
└─► P14: Delete TenantService.Api.Tenancy/ directory (7 files), remove DI registrations

Platform Cleanup
│
└─► P15: Remove dead RequestContext fields (UserName, Roles, IpAddress, UserAgent)
         Delete ITenantContext
```

### Correct Execution Order

The dependency graph resolves to this linear sequence:

| Step | Phase | Description | Depends On |
|------|-------|-------------|------------|
| 1 | ADR-014 | Add DepartmentId/RoleId to ICurrentPrincipal + CurrentPrincipal | — |
| 2 | ADR-014 | Parse department_id/role_id in CurrentPrincipalFactory | Step 1 |
| 3 | ADR-014 | Add GetActiveRoleForUser endpoint to AuthorizationService | — |
| 4 | ADR-014 | Update LoginCommandHandler: resolve Role, pass deptId/roleId to JWT | Steps 2,3 |
| 5 | ADR-014 | Update HmacJwtTokenGenerator: accept departmentId, roleId params | Step 4 |
| 6 | ADR-014 | Remove User.DepartmentId, Session.DepartmentId | Steps 4,5 |
| 7 | ADR-014 | Remove ChangeDepartment use case (handler, command, endpoint) | Step 6 |
| 8 | ADR-014 | Remove AssignRole/ChangeUserRole department cross-checks | Step 6 |
| 9 | ADR-012 | Fix TenantService DI registration (broken cast) | — |
| 10 | ADR-012 | Remove RequestContext from 12 Authz command records | — |
| 11 | ADR-012 | Migrate 12 IdentityService handlers to Platform accessor | — |
| 12 | ADR-012 | Migrate IdentityService accessor + DI to single interface | Step 11 |
| 13 | ADR-012 | Migrate AuthorizationService accessor + DI to single interface | Step 10 |
| 14 | ADR-012 | Remove all local IRequestContext/RequestContext/TenantSource | Steps 9,12,13 |
| 15 | ADR-012 | Remove dead RequestContext fields (UserName, Roles) | Step 14 |
| 16 | ADR-013 | Delete TenantService.Api.Tenancy/ (7 files) + DI | Step 9 |
| 17 | ADR-012 | Delete ITenantContext (orphaned) | — |
| 18 | ADR-012 | Build + full verification | All |

---

# PHASE 3 — Business Model Validation

### Tenant → Department → Role → Permissions
**Validated.** `TenantService` owns Department. `AuthorizationService` owns Role, Permission, Grant, RoleAssignment. Boundaries are correct.

### User belongs to exactly one Tenant
**Validated.** `User.TenantId` is set at registration, immutable.

### User can have exactly one Role
**Validated.** `AssignRoleCommandHandler` checks `existingActive.Count > 0` and rejects if the user already has an active assignment. `ChangeUserRole` revokes the old before creating the new.

### User is NOT directly assigned to a Department
**VIOLATION in current code.** `User.DepartmentId` exists and is used. ADR-014 mandates removal.

### Department Membership is derived from Role
**NOT YET IMPLEMENTED.** Currently: User → Department (direct). Target: User → RoleAssignment → Role → Department.

### ChangeDepartment must not exist
**VIOLATION in current code.** `ChangeDepartmentCommandHandler` exists. ADR-014 mandates removal.

### IdentityService does NOT own Department Membership
**VIOLATION in current code.** `User.DepartmentId` + `ChangeDepartment` = IdentityService owns membership. ADR-014 mandates removal.

### AuthorizationService owns Department Membership through Role Assignment
**Validated in data model** (`Role.DepartmentId` exists, immutable). Not yet wired for JWT derivation.

---

# PHASE 4 — Implementation Plan

## Phase A: Platform Principal Enrichment (ADR-014 foundation)

**Goal:** Add `DepartmentId` and `RoleId` to `ICurrentPrincipal` so the propagation pipeline can carry them from JWT to RequestContext without ad-hoc claim parsing.

**Affected projects:** Platform.Abstractions, Platform.Infrastructure

**Affected files:**
- `src/Platform/Platform.Abstractions/Principal/ICurrentPrincipal.cs` — add `Guid? DepartmentId`, `Guid? RoleId`
- `src/Platform/Platform.Abstractions/Principal/CurrentPrincipal.cs` — add properties
- `src/Platform/Platform.Infrastructure/Principal/CurrentPrincipalFactory.cs` — parse `department_id`, `role_id` claims

**Expected outcome:** `CurrentPrincipalFactory` parses `department_id` and `role_id` once. All downstream components read from principal.

**Validation:** Build compiles. Existing behavior preserved (fields are null until JWT carries them).

**Rollback risk:** Low — additive only, no existing code breaks.

**Blocking dependencies:** None.

## Phase B: AuthorizationService Role Resolution Endpoint (ADR-014)

**Goal:** Expose an endpoint that IdentityService can call during login to resolve a user's active Role and its DepartmentId.

**Affected projects:** AuthorizationService.Application, AuthorizationService.Infrastructure, AuthorizationService.Api, IdentityService.Infrastructure

**Affected files:**
- `src/Services/AuthorizationService/AuthorizationService.Domain/Repositories/IRoleAssignmentRepository.cs` — add `GetActiveRoleForSubjectAsync`
- `src/Services/AuthorizationService/AuthorizationService.Infrastructure/Persistence/Repositories/RoleAssignmentRepository.cs` — implement query
- `src/Services/AuthorizationService/AuthorizationService.Api/Endpoints/` — new endpoint or enrich existing
- `src/Services/IdentityService/IdentityService.Infrastructure/` — `IAuthorizationServiceClient` or similar, add `GetActiveRoleForUserAsync`

**Expected outcome:** IdentityService can call AuthorizationService to get a user's Role.DepartmentId during login.

**Validation:** Endpoint returns correct Role for a user with an active assignment. Returns null/empty for unassigned users.

**Rollback risk:** Low — new endpoint, no existing behavior changed.

**Blocking dependencies:** None (can be built in parallel with Phase A).

## Phase C: Login Pipeline Update (ADR-014)

**Goal:** Login resolves DepartmentId from Role (via AuthorizationService), not from User.

**Affected projects:** IdentityService.Application, IdentityService.Infrastructure

**Affected files:**
- `src/Services/IdentityService/IdentityService.Application/Features/Login/LoginCommandHandler.cs` — call AuthorizationService to resolve Role, pass departmentId to JWT generator
- `src/Services/IdentityService/IdentityService.Infrastructure/Crypto/HmacJwtTokenGenerator.cs` — `GenerateAccessToken` signature adds `Guid? departmentId`, `Guid? roleId`; writes `department_id` and `role_id` claims instead of reading from User

**Expected outcome:** JWT `department_id` claim originates from `Role.DepartmentId`, not `User.DepartmentId`.

**Validation:** Login produces JWT with `department_id` matching the user's assigned Role's department.

**Rollback risk:** Medium — changes JWT shape; all services consuming JWT must handle new claims gracefully.

**Blocking dependencies:** Phases A and B.

## Phase D: Remove User.DepartmentId (ADR-014)

**Goal:** Eliminate the redundant DepartmentId from User aggregate and Session.

**Affected projects:** IdentityService.Domain, IdentityService.Application, IdentityService.Infrastructure, IdentityService.Api

**Affected files:**
- `src/Services/IdentityService/IdentityService.Domain/Aggregates/User/User.cs` — remove `DepartmentId` property, remove `ChangeDepartment` method
- `src/Services/IdentityService/IdentityService.Domain/Aggregates/Session/Session.cs` — remove `DepartmentId` parameter
- `src/Services/IdentityService/IdentityService.Application/Features/Login/LoginCommandHandler.cs` — remove `user.DepartmentId` from Session.Create
- `src/Services/IdentityService/IdentityService.Application/Features/Register/RegisterCommandHandler.cs` — remove DepartmentId from registration (or keep as tenant-scoped validation only)
- `src/Services/IdentityService/IdentityService.Application/Features/ChangeDepartment/` — DELETE entire directory
- `src/Services/IdentityService/IdentityService.Api/Endpoints/AuthEndpoints.cs` — remove ChangeDepartment endpoint
- `src/Services/IdentityService/IdentityService.Infrastructure/Persistence/IdentityDbContext.cs` — remove DepartmentId index/property config
- `src/Services/IdentityService/IdentityService.Infrastructure/Caching/CachedUserRepository.cs` — remove DepartmentId from DTO
- `src/Services/IdentityService/IdentityService.Application/Features/GetUser/GetUserQueryHandler.cs` — remove DepartmentId from response

**Expected outcome:** User aggregate no longer has DepartmentId. Membership is derived from Role.

**Validation:** Build compiles. Registration still works (without DepartmentId). Login produces valid JWT.

**Rollback risk:** High — database migration required (drop column). Irreversible without data restore.

**Blocking dependencies:** Phase C (JWT must originate from Role before User.DepartmentId is removed).

## Phase E: Cleanup AuthorizationService Department Cross-Checks (ADR-014)

**Goal:** Remove department-matching checks that compared User.DepartmentId with Role.DepartmentId, since User no longer has DepartmentId.

**Affected projects:** AuthorizationService.Application

**Affected files:**
- `src/Services/AuthorizationService/AuthorizationService.Application/Features/AssignRole/AssignRoleCommandHandler.cs` — remove `command.Context.DepartmentId` check (line 26)
- `src/Services/AuthorizationService/AuthorizationService.Application/Features/ChangeUserRole/ChangeUserRoleCommandHandler.cs` — remove `command.Context.DepartmentId` check (line 37), remove same-department check (line 44) or replace with Role-based check

**Expected outcome:** Authorization handlers no longer depend on caller's DepartmentId from RequestContext.

**Validation:** AssignRole and ChangeUserRole still enforce department constraints via Role.DepartmentId comparison.

**Rollback risk:** Low — removing a validation that will no longer have data to check.

**Blocking dependencies:** Phase D.

## Phase F: Fix TenantService DI + Delete Dead Code (ADR-012 + ADR-013)

**Goal:** Fix the broken DI registration in TenantService. Delete dead Tenancy code. Delete local types.

**Affected projects:** TenantService.Api, TenantService.Application

**Affected files:**
- `src/Services/TenantService/TenantService.Api/Extensions/ServiceCollectionExtensions.cs` — replace dual registration with single `Platform.Abstractions.Tenant.IRequestContextAccessor` registration; remove TenantHostOptions binding; remove ITenantResolver/ITenantResolutionProvider registrations
- `src/Services/TenantService/TenantService.Api/Tenancy/` — DELETE entire directory (7 files)
- `src/Services/TenantService/TenantService.Application/Common/Abstractions/IRequestContextAccessor.cs` — DELETE
- `src/Services/TenantService/TenantService.Application/Common/Abstractions/RequestContext.cs` — DELETE
- `src/Services/TenantService/TenantService.Application/Common/Abstractions/TenantSource.cs` — DELETE

**Expected outcome:** TenantService has single Platform accessor registration. Dead Tenancy code removed. Local types removed.

**Validation:** Build compiles. DI resolves correctly. TenantBehavior/TenantRlsInterceptor still work.

**Rollback risk:** Medium — DI registration change affects runtime resolution.

**Blocking dependencies:** None (can be done in parallel with ADR-014 phases).

## Phase G: AuthorizationService RequestContext Decoupling (ADR-012 Phase 0)

**Goal:** Remove `RequestContext` from all 12 Authorization command/query records. Handlers inject Platform accessor instead.

**Affected projects:** AuthorizationService.Application, AuthorizationService.Api, AuthorizationService.Infrastructure

**Affected files:**
- 12 command/query records: `AssignRoleCommand`, `ChangeUserRoleCommand`, `CreateRoleCommand`, `CreatePermissionCommand`, `GrantPermissionCommand`, `RevokeRoleCommand`, `RevokePermissionCommand`, `GetEffectivePermissionsQuery`, `InvalidateAuthorizationCacheCommand`, `EvaluateAuthorizationDecisionCommand`, `EvaluateBatchAuthorizationDecisionsCommand`, `RecordOperationResultCommand`
- 12+ command/query validators: remove `command.Context.TenantId` rules
- 12+ command handlers: inject `Platform.Abstractions.Tenant.IRequestContextAccessor`, read from accessor
- `src/Services/AuthorizationService/AuthorizationService.Api/Endpoints/` — update endpoint callers (stop passing Context in command construction)
- `src/Services/AuthorizationService/AuthorizationService.Application/Common/Abstractions/` — delete local IRequestContext, RequestContext

**Expected outcome:** Authz commands carry only business data. Context is ambient via accessor.

**Validation:** All authorization operations still function correctly. RLS enforcement unchanged.

**Rollback risk:** Medium — large surface area, but mechanical transformation.

**Blocking dependencies:** Phase A (principal must carry DepartmentId before commands stop carrying it).

## Phase H: IdentityService Consumer Migration (ADR-012 Phase 3)

**Goal:** Migrate all 12 IdentityService handlers from local IRequestContextAccessor to Platform accessor.

**Affected projects:** IdentityService.Application, IdentityService.Infrastructure

**Affected files:**
- 12 handlers: Login, Register, ChangeDepartment (will be deleted in Phase D), RefreshToken, Logout, MfaVerify, EnableMfa, UnlockUser, DeleteUser, DisableUser, ActivateUser, GetUser
- `src/Services/IdentityService/IdentityService.Infrastructure/Persistence/IdentityDbContext.cs` — migrate to Platform accessor
- `src/Services/IdentityService/IdentityService.Infrastructure/Persistence/IdentityDbContextFactory.cs` — migrate NullRequestContextAccessor

**Expected outcome:** All IdentityService consumers depend on Platform.Abstractions.Tenant.IRequestContextAccessor.

**Validation:** Build compiles. All handlers resolve correct context.

**Rollback risk:** Low — mechanical using-directive + property rename changes.

**Blocking dependencies:** None (can be done in parallel with ADR-014 phases).

## Phase I: IdentityService Accessor + DI Cleanup (ADR-012 Phase 4-5)

**Goal:** Single accessor implementation per service. Remove local types.

**Affected projects:** IdentityService.Api, IdentityService.Application

**Affected files:**
- `src/Services/IdentityService/IdentityService.Api/Infrastructure/HttpRequestContextAccessor.cs` — rewrite to single Platform interface
- `src/Services/IdentityService/IdentityService.Api/Extensions/ServiceCollectionExtensions.cs` — single registration
- `src/Services/IdentityService/IdentityService.Application/Common/Abstractions/IRequestContextAccessor.cs` — DELETE
- `src/Services/IdentityService/IdentityService.Application/Common/Abstractions/RequestContext.cs` — DELETE
- `src/Services/IdentityService/IdentityService.Application/Common/Abstractions/TenantSource.cs` — DELETE

**Expected outcome:** IdentityService has single Platform accessor. Local types removed.

**Validation:** Build compiles. DI resolves correctly.

**Rollback risk:** Medium.

**Blocking dependencies:** Phase H.

## Phase J: Platform Cleanup (ADR-012 + ADR-014)

**Goal:** Remove dead fields from RequestContext. Delete orphaned ITenantContext.

**Affected projects:** Platform.Abstractions

**Affected files:**
- `src/Platform/Platform.Abstractions/Tenant/RequestContext.cs` — remove `UserName`, `Roles`
- `src/Platform/Platform.Abstractions/Tenant/ITenantContext.cs` — DELETE

**Expected outcome:** RequestContext contains only fields that are actually populated and consumed.

**Validation:** Build compiles. All accessor implementations still produce valid RequestContext.

**Rollback risk:** Low — removing unused fields.

**Blocking dependencies:** Phases F, I (all accessors must stop populating dead fields first).

## Phase K: Full Verification

**Goal:** Build entire solution. Verify DI containers. Verify RLS. Verify JWT shape.

**Validation steps:**
1. `dotnet build` entire solution — zero errors
2. Each service host starts without DI exceptions
3. JWT contains `department_id` from Role, `role_id`, `tenant_id`, `sub`
4. `CurrentPrincipalFactory` parses all claims into principal
5. `RequestContext` is populated from principal (no ad-hoc claim parsing)
6. `TenantRlsInterceptor` sets `app.current_tenant_id` and `app.current_department_id`
7. `IdentityDbContext` RLS filter works for `IDepartmentBound` entities
8. `TenantDbContext` RLS filter works
9. No service-local IRequestContext/RequestContext/TenantSource types remain
10. No dead Tenancy/ files remain

---

# PHASE 5 — Master TODO Document

See separate file: `docs/adr/TODO-Architecture-Execution-Plan.md`

---

# PHASE 6 — Final Validation

### 1. Is every ADR internally consistent?

**ADR-012:** Yes, with the corrections noted in Phase 1 (skip TenantSource, remove dead fields).

**ADR-013:** Yes, fully consistent. No changes needed to the ADR itself. The "Allowed Resolution Points" section will need a note that login additionally resolves DepartmentId via AuthorizationService, but this is an implementation detail, not a rule change.

**ADR-014:** Yes, fully consistent. Supersedes specific field assumptions in ADR-012.

### 2. Is any ADR obsolete?

No. All three ADRs address distinct concerns:
- ADR-012: RequestContext consolidation (interface/record ownership)
- ADR-013: TenantId propagation rules (no re-resolution post-auth)
- ADR-014: Department Membership derivation (from Role, not User)

### 3. Is any implementation phase unnecessary?

**ADR-012 Phase 1 (Introduce Platform.TenantSource):** Unnecessary. TenantSource is only used by the dead Tenancy providers. Skip.

**ADR-012 Phase 2 (Reduce to compatibility aliases):** Unnecessary if we go directly from current state to single-interface. The alias phase was designed for a gradual rollout across all three services simultaneously. Since we can migrate each service independently and delete local types at the end, we can skip the alias step.

### 4. Is any task duplicated?

No. Each task appears exactly once in the execution order.

### 5. Can implementation begin without further architectural work?

**Yes.** All three ADRs are accepted/proposed with clear decisions. The execution plan is complete. The dependency graph is resolved. No open architectural questions block implementation.

**One recommendation:** ADR-014 should be updated from "Proposed" to "Accepted" before implementation begins, to establish it as a binding architectural decision.
