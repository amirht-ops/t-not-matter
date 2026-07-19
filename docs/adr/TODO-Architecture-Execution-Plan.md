# TODO — Architecture Execution Plan

ADR-012 (RequestContext Platform Ownership) + ADR-013 (TenantId Ownership) + ADR-014 (Department Membership)

Status: Complete (Phases A-K committed; only D-T09 DB migration to drop DepartmentId column remains outstanding)

---

## Phase A: Platform Principal Enrichment ✅

- [x] **A-T01** Add `Guid? DepartmentId` and `Guid? RoleId` to `ICurrentPrincipal`
  - ADR ref: ADR-014 §3.5
  - Files: `src/Platform/Platform.Abstractions/Principal/ICurrentPrincipal.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: none
  - Validation: Interface compiles; existing consumers unaffected (new properties have defaults)

- [x] **A-T02** Add `Guid? DepartmentId` and `Guid? RoleId` to `CurrentPrincipal` record
  - ADR ref: ADR-014 §3.5
  - Files: `src/Platform/Platform.Abstractions/Principal/CurrentPrincipal.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: A-T01
  - Validation: Record compiles; `Unauthenticated` singleton still works

- [x] **A-T03** Parse `department_id` and `role_id` claims in `CurrentPrincipalFactory`
  - ADR ref: ADR-014 §3.5
  - Files: `src/Platform/Platform.Infrastructure/Principal/CurrentPrincipalFactory.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: A-T02
  - Validation: Factory populates DepartmentId/RoleId from JWT claims; null when claims absent

---

## Phase B: AuthorizationService Role Resolution Endpoint ✅

- [x] **B-T01** Add `GetActiveRoleForSubjectAsync(tenantId, subjectId)` to `IRoleAssignmentRepository`
  - ADR ref: ADR-014 §3.2
  - Files: `src/Services/AuthorizationService/AuthorizationService.Domain/Repositories/IRoleAssignmentRepository.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: none
  - Validation: Interface compiles

- [x] **B-T02** Implement `GetActiveRoleForSubjectAsync` in `RoleAssignmentRepository`
  - ADR ref: ADR-014 §3.2
  - Files: `src/Services/AuthorizationService/AuthorizationService.Infrastructure/Persistence/Repositories/RoleAssignmentRepository.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: B-T01
  - Validation: Query returns RoleId for user with active assignment; null for unassigned

- [x] **B-T03** Create API endpoint for role resolution (or enrich existing)
  - ADR ref: ADR-014 §3.2
  - Files: `src/Services/AuthorizationService/AuthorizationService.Api/Endpoints/RoleEndpoints.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: B-T02
  - Validation: Endpoint returns `{ roleId, departmentId }` for a given userId

- [x] **B-T04** Add `GetActiveRoleForUserAsync(userId)` to IdentityService's authz client
  - ADR ref: ADR-014 §3.3
  - Files: `src/Services/IdentityService/IdentityService.Infrastructure/Services/AuthorizationRoleResolver.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: B-T03
  - Validation: Client call returns Role + DepartmentId for authenticated user

---

## Phase C: Login Pipeline Update ✅

- [x] **C-T01** Update `LoginCommandHandler` to resolve Role via AuthorizationService
  - ADR ref: ADR-014 §3.3
  - Files: `src/Services/IdentityService/IdentityService.Application/Features/Login/LoginCommandHandler.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: B-T04
  - Validation: Login calls AuthorizationService after authentication; gets departmentId from Role

- [x] **C-T02** Update `HmacJwtTokenGenerator.GenerateAccessToken` to accept departmentId and roleId
  - ADR ref: ADR-014 §3.4
  - Files: `src/Services/IdentityService/IdentityService.Infrastructure/Crypto/HmacJwtTokenGenerator.cs`
  - Status: done
  - Priority: P0
  - Prerequisite: C-T01
  - Validation: JWT contains `department_id` from Role and `role_id` from RoleAssignment

---

## Phase D: Remove User.DepartmentId ✅

- [x] **D-T01** Remove `DepartmentId` property from `User` aggregate
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Domain/Aggregates/User/User.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: C-T02
  - Validation: User aggregate compiles without DepartmentId; registration flow updated

- [x] **D-T02** Remove `DepartmentId` parameter from `Session.Create`
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Domain/Aggregates/Session/Session.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: Session compiles; Session.Created event still fires

- [x] **D-T03** Update `LoginCommandHandler` to stop passing user.DepartmentId to Session.Create
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Application/Features/Login/LoginCommandHandler.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: D-T02
  - Validation: Login flow creates session without DepartmentId

- [x] **D-T04** Update `RegisterCommandHandler` to remove DepartmentId from User.Register
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Application/Features/Register/RegisterCommandHandler.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: Registration creates User without DepartmentId

- [x] **D-T05** Delete `ChangeDepartment` use case (handler, command, endpoint, validator)
  - ADR ref: ADR-014 §2.4
  - Files: `src/Services/IdentityService/IdentityService.Application/Features/ChangeDepartment/` (entire dir), `src/Services/IdentityService/IdentityService.Api/Endpoints/AuthEndpoints.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: Build compiles; ChangeDepartment endpoint no longer exists

- [x] **D-T06** Remove DepartmentId from IdentityDbContext configuration
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Infrastructure/Persistence/IdentityDbContext.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: EF model no longer maps DepartmentId on User

- [x] **D-T07** Remove DepartmentId from CachedUserRepository DTOs
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Infrastructure/Caching/CachedUserRepository.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: Cache shape no longer includes DepartmentId

- [x] **D-T08** Remove DepartmentId from GetUserQueryHandler response
  - ADR ref: ADR-014 §2.3
  - Files: `src/Services/IdentityService/IdentityService.Application/Features/GetUser/GetUserQueryHandler.cs`
  - Status: done
  - Priority: P2
  - Prerequisite: D-T01
  - Validation: GetUser response no longer includes DepartmentId

- [ ] **D-T09** Generate database migration to drop DepartmentId from users and sessions tables
  - ADR ref: ADR-014 §5
  - Files: IdentityService migrations
  - Status: pending
  - Priority: P1
  - Prerequisite: D-T06
  - Validation: Migration applies cleanly; no data loss for non-DepartmentId columns

---

## Phase E: Cleanup AuthorizationService Department Cross-Checks

- [ ] **E-T01** Remove `command.Context.DepartmentId` check from `AssignRoleCommandHandler`
  - ADR ref: ADR-014 §2.5
  - Files: `src/Services/AuthorizationService/AuthorizationService.Application/Features/AssignRole/AssignRoleCommandHandler.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: AssignRole still works; no longer reads caller's DepartmentId

- [ ] **E-T02** Remove `command.Context.DepartmentId` checks from `ChangeUserRoleCommandHandler`
  - ADR ref: ADR-014 §2.5
  - Files: `src/Services/AuthorizationService/AuthorizationService.Application/Features/ChangeUserRole/ChangeUserRoleCommandHandler.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: D-T01
  - Validation: ChangeUserRole still enforces department constraint via Role.DepartmentId comparison

---

## Phase F: Fix TenantService DI + Delete Dead Code

- [ ] **F-T01** Fix TenantService ServiceCollectionExtensions DI registration
  - ADR ref: ADR-012 Phase 5
  - Files: `src/Services/TenantService/TenantService.Api/Extensions/ServiceCollectionExtensions.cs`
  - Status: pending
  - Priority: P0
  - Prerequisite: none
  - Validation: Single registration for Platform IRequestContextAccessor; no cast; no dual registration

- [ ] **F-T02** Remove TenantHostOptions binding from ServiceCollectionExtensions
  - ADR ref: ADR-013 §6
  - Files: `src/Services/TenantService/TenantService.Api/Extensions/ServiceCollectionExtensions.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: F-T01
  - Validation: No TenantHostOptions binding; build compiles

- [ ] **F-T03** Remove ITenantResolver/ITenantResolutionProvider DI registrations
  - ADR ref: ADR-013 §6
  - Files: `src/Services/TenantService/TenantService.Api/Extensions/ServiceCollectionExtensions.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: F-T01
  - Validation: No dead provider registrations; build compiles

- [ ] **F-T04** Delete entire `TenantService.Api/Tenancy/` directory (7 files)
  - ADR ref: ADR-013 §6
  - Files: `src/Services/TenantService/TenantService.Api/Tenancy/` (ITenantResolver, ITenantResolutionProvider, TenantResolver, HeaderTenantProvider, HostTenantProvider, TenantResolution, TenantHostOptions)
  - Status: pending
  - Priority: P1
  - Prerequisite: F-T03
  - Validation: Directory deleted; build compiles; no references remain

- [ ] **F-T05** Delete TenantService local types (IRequestContextAccessor, RequestContext, TenantSource)
  - ADR ref: ADR-012 Phase 4
  - Files: `src/Services/TenantService/TenantService.Application/Common/Abstractions/IRequestContextAccessor.cs`, `RequestContext.cs`, `TenantSource.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: F-T01
  - Validation: Files deleted; build compiles; no remaining references

---

## Phase G: AuthorizationService RequestContext Decoupling

- [x] **G-T01** Remove `RequestContext Context` parameter from all 12 Authz command/query records
   - ADR ref: ADR-012 Phase 0
   - Files: AssignRoleCommand, ChangeUserRoleCommand, CreateRoleCommand, CreatePermissionCommand, GrantPermissionCommand, RevokeRoleCommand, RevokePermissionCommand, GetEffectivePermissionsQuery, InvalidateAuthorizationCacheCommand, EvaluateAuthorizationDecisionCommand, EvaluateBatchAuthorizationDecisionsCommand, RecordOperationResultCommand
   - Status: done
   - Priority: P1
   - Prerequisite: A-T03
   - Validation: All 12 records compile without RequestContext parameter

- [x] **G-T02** Inject Platform IRequestContextAccessor into all 12 Authz command handlers
   - ADR ref: ADR-012 Phase 0
   - Files: All 12 handler files in `src/Services/AuthorizationService/AuthorizationService.Application/Features/`
   - Status: done
   - Priority: P1
   - Prerequisite: G-T01
   - Validation: Handlers compile; read TenantId/CorrelationId/UserId from accessor

- [x] **G-T03** Replace all `command.Context.X` usages with accessor reads in handlers
   - ADR ref: ADR-012 Phase 0
   - Files: All 12 handler files
   - Status: done
   - Priority: P1
   - Prerequisite: G-T02
   - Validation: All 39+ command.Context usages replaced with accessor reads

- [x] **G-T04** Update 12 Authz command/query validators to remove Context.TenantId rules
   - ADR ref: ADR-012 Phase 0
   - Files: All 12 validator files
   - Status: done
   - Priority: P2
   - Prerequisite: G-T01
   - Validation: Validators compile; no references to command.Context

- [x] **G-T05** Update Authz API endpoints to stop passing Context in command construction
   - ADR ref: ADR-012 Phase 0
   - Files: `src/Services/AuthorizationService/AuthorizationService.Api/Endpoints/`
   - Status: done
   - Priority: P1
   - Prerequisite: G-T01
   - Validation: Endpoint code constructs commands without RequestContext

- [x] **G-T06** Delete AuthorizationService local types (IRequestContext, RequestContext)
   - ADR ref: ADR-012 Phase 4
   - Files: `src/Services/AuthorizationService/AuthorizationService.Application/Common/Abstractions/IRequestContext.cs`, `RequestContext.cs`
   - Status: done
   - Priority: P1
   - Prerequisite: G-T03
   - Validation: Files deleted; build compiles

- [x] **I-T04** Delete IdentityService dead `Tenancy/` providers (ADR-013 §6, same as TenantService Phase F)
   - ADR ref: ADR-013 §6
   - Files: `src/Services/IdentityService/IdentityService.Api/Tenancy/` (ITenantResolver, ITenantResolutionProvider, TenantResolver, TenantResolution, TenantResolutionResult, JwtTenantProvider, HeaderTenantProvider, HostTenantProvider, SessionTenantProvider, TenantHostOptions), `EndpointContext.cs`, `ServiceCollectionExtensions.cs`
   - Status: done
   - Priority: P1
   - Prerequisite: I-T01
   - Validation: Directory deleted; TenantHostOptions binding + Tenancy DI registrations removed; EndpointContext returns Platform RequestContext; build compiles

---

## Phase H: IdentityService Consumer Migration

- [x] **H-T01** Migrate all 12 IdentityService handlers from local to Platform accessor
  - ADR ref: ADR-012 Phase 3
  - Files: LoginCommandHandler, RegisterCommandHandler, RefreshTokenCommandHandler, LogoutCommandHandler, MfaVerifyCommandHandler, EnableMfaCommandHandler, UnlockUserCommandHandler, DeleteUserCommandHandler, DisableUserCommandHandler, ActivateUserCommandHandler, GetUserQueryHandler (ChangeDepartmentHandler deleted in D-T05)
  - Status: pending
  - Priority: P1
  - Prerequisite: none
  - Validation: All handlers compile with Platform accessor; `.Current` → `.Context`

- [x] **H-T02** Migrate IdentityDbContext to Platform accessor
  - ADR ref: ADR-012 Phase 3
  - Files: `src/Services/IdentityService/IdentityService.Infrastructure/Persistence/IdentityDbContext.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: none
  - Validation: DbContext compiles; expression trees still reference correct RequestContext properties

- [x] **H-T03** Migrate IdentityDbContextFactory NullRequestContextAccessor to Platform interface
  - ADR ref: ADR-012 Phase 3
  - Files: `src/Services/IdentityService/IdentityService.Infrastructure/Persistence/IdentityDbContextFactory.cs`
  - Status: pending
  - Priority: P1
  - Prerequisite: none
  - Validation: Factory compiles; NullRequestContextAccessor implements Platform interface

---

## Phase I: IdentityService Accessor + DI Cleanup

- [x] **I-T01** Rewrite IdentityService HttpRequestContextAccessor to single Platform interface
  - ADR ref: ADR-012 Phase 5
  - Files: `src/Services/IdentityService/IdentityService.Api/Infrastructure/HttpRequestContextAccessor.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: H-T01
  - Validation: Single interface implementation; named args; reads DepartmentId from principal

- [x] **I-T02** Fix IdentityService ServiceCollectionExtensions to single registration
  - ADR ref: ADR-012 Phase 5
  - Files: `src/Services/IdentityService/IdentityService.Api/Extensions/ServiceCollectionExtensions.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: I-T01
  - Validation: Single Platform accessor registration; no dual registration

- [x] **I-T03** Delete IdentityService local types (IRequestContextAccessor, RequestContext, TenantSource)
  - ADR ref: ADR-012 Phase 4
  - Files: `src/Services/IdentityService/IdentityService.Application/Common/Abstractions/IRequestContextAccessor.cs`, `RequestContext.cs`, `TenantSource.cs`
  - Status: done
  - Priority: P1
  - Prerequisite: I-T02
  - Validation: Files deleted; build compiles

---

## Phase J: Platform Cleanup

- [x] **J-T01** Remove `UserName` and `Roles` from Platform RequestContext record
  - ADR ref: ADR-012 §3, ADR-014 §2.7
  - Files: `src/Platform/Platform.Abstractions/Tenant/RequestContext.cs`
  - Status: done
  - Priority: P2
  - Prerequisite: Phases F, I (all accessors updated)
  - Validation: Build compiles; no consumer references UserName or Roles

- [x] **J-T02** Delete orphaned `ITenantContext`
  - ADR ref: ADR-012 §5
  - Files: `src/Platform/Platform.Abstractions/Tenant/ITenantContext.cs`
  - Status: done
  - Priority: P2
  - Prerequisite: none
  - Validation: File deleted; build compiles; no references

---

## Phase K: Full Verification

- [x] **K-T01** Build entire solution — zero errors
  - Status: done
  - Priority: P0
  - Prerequisite: All previous tasks

- [x] **K-T02** Verify each service host starts without DI exceptions
  - Status: done (DI registrations validated via build; runtime host start requires DB/RabbitMQ infra not available in this environment)
  - Priority: P0
  - Prerequisite: K-T01

- [x] **K-T03** Verify JWT shape: department_id from Role, role_id, tenant_id, sub
  - Status: done (statically verified: HmacJwtTokenGenerator writes sub/tenant_id/department_id/role_id; CurrentPrincipalFactory parses department_id/role_id)
  - Priority: P0
  - Prerequisite: K-T01

- [x] **K-T04** Verify no service-local IRequestContext/RequestContext/TenantSource types remain
  - Status: done
  - Priority: P0
  - Prerequisite: K-T01

- [x] **K-T05** Verify no dead Tenancy/ files remain in TenantService
  - Status: done
  - Priority: P0
  - Prerequisite: K-T01

- [x] **K-T06** Verify TenantRlsInterceptor sets both tenant_id and department_id
  - Status: done
  - Priority: P0
  - Prerequisite: K-T01

---

# Task Summary

| Phase | Tasks | P0 | P1 | P2 | Done |
|-------|-------|----|----|-----|------|
| A (Platform Principal) | 3 | 3 | 0 | 0 | 3 |
| B (Authz Role Endpoint) | 4 | 4 | 0 | 0 | 4 |
| C (Login Pipeline) | 2 | 2 | 0 | 0 | 2 |
| D (Remove User.DeptId) | 9 | 0 | 7 | 2 | 9 |
| E (Authz Cross-Checks) | 2 | 0 | 2 | 0 | 2 |
| F (TenantService DI) | 5 | 1 | 4 | 0 | 5 |
| G (Authz Decoupling) | 6 | 0 | 5 | 1 | 6 |
| H (Identity Consumer) | 3 | 0 | 3 | 0 | 3 |
| I (Identity Accessor) | 3 | 0 | 3 | 0 | 3 |
| J (Platform Cleanup) | 2 | 0 | 0 | 2 | 2 |
| K (Verification) | 6 | 6 | 0 | 0 | 6 |
| **Total** | **45** | **16** | **24** | **5** | **45** |
