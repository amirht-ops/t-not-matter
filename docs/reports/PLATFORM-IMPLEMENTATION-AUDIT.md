# Platform Implementation Audit

- **Date:** 2026-07-14
- **Author:** Principal Software Engineer (read-only verification pass)
- **Scope:** Validate all completed work — ADR implementations, Authentication Contract, RequestContext, Tenant propagation, Outbox, RabbitMQ/Messaging, Department flow, Integration events, CurrentPrincipal, Middleware, Platform abstractions, Service boundaries — for architectural/internal consistency, production readiness, and integration.
- **Method:** Static read-only review only. No code, configuration, or documentation files were modified during this audit. Findings are evidence-based with `file:line` references. Five parallel domain reviews were performed (Messaging/Events, RequestContext/Tenant, Platform/DI, Auth/Code-Debt, ADR Compliance) plus a full-tree grep for `TODO|FIXME|HACK|XXX|NotImplementedException|#if DEBUG`.
- **Baseline:** `docs/architecture/RABBITMQ-ARCHITECTURE-REPORT.md`, `docs/architecture/MESSAGING-IMPLEMENTATION-PLAN.md`, `docs/todo/MESSAGING-TODO.md`, `docs/reports/PHASE-1-IMPLEMENTATION-REPORT.md`, `docs/reports/INCONSISTENCY-REPORT-ADR017.md`, and `docs/adr/*`.

---

## 1. Executive Summary

The **Phase 1 messaging remediation (MSG-001..004, commit `70b00d8`) is complete, correct, and internally consistent**. The previously-Critical dead `DepartmentSoftDeleteConsumer` defect is resolved and the end-to-end flow is verified. MassTransit configuration is now consistent across all three services. No `TODO`/`FIXME`/`HACK`/not-implemented markers exist anywhere in `src/`.

However, the broader platform carries **multiple High-severity issues unrelated to Phase 1** that block production readiness:

1. **Tenant isolation (RLS) is not reliably scoped in background/consumer code** — a captive-dependency singleton `IRequestContextAccessor` means hosted services, MassTransit consumers, and outbox dispatchers run with no `SET LOCAL app.current_tenant_id`, i.e. **no row-level tenant scoping** on background DB work. This is currently masked only because individual consumers re-scope explicitly via `envelope.TenantId`; it is a latent security/isolation trap.
2. **Hardcoded development signing key** is compiled into code and present in every `appsettings.json` (Secret Store not wired).
3. **IdentityService diverges from the shared platform bootstrap** — it omits `AddPlatformMiddleware`, `AddPlatformAuthorizationPolicies`, and `AddPlatformInfrastructure` (yet calls `UsePlatformMiddleware`), so middleware options/policies may run with defaults.

Medium/Low findings are dominated by **duplicated/dead abstractions** (`ICacheService`, outbox repositories, `DeadLetterMessage`, `AddPlatformAuthorization`, `IOutboxProcessor`), **orphaned integration events** (produced-but-unconsumed, causing stale caches), **documentation drift** (ADR-017 stale; RABBITMQ report and the inconsistency report assert pre-remediation state as current), and **inconsistent naming/options** (`PascalCaseV1` vs `kebab-case.v1`, four `JwtOptions` types).

**Verdict:** Phase 1 = **GO** (no blockers). Platform production readiness = **NO-GO** until the High items are remediated. Detail in §10.

---

## 2. Verified Implementations

| Area | Status | Evidence |
|------|--------|----------|
| **MSG-001/002** — `DepartmentSoftDeletedV1` produced & raised | ✅ Verified | `TenantDomainEvents.cs:118-128` (`EventTypeName => "DepartmentSoftDeletedV1"`); `Department.cs:114` (`SoftDelete` raises it); `DeleteDepartmentCommandHandler.cs:25,31`. `DepartmentStatusChangedV1` preserved. |
| **MSG-003** — IdentityService retry/quorum/DLQ | ✅ Verified | `IdentityService...ServiceCollectionExtensions.cs:158` (`Exponential(10,1s,30s,5s)`); `:163-180` three `ReceiveEndpoint` each `SetQuorumQueue()` + `BindDeadLetterQueue`. `ConfigureEndpoints` removed. |
| **MSG-004** — dead switch cases removed | ✅ Verified | `AuthorizationCacheInvalidationConsumer.cs:35-58` — no `UserLoggedInV1`/`MfaVerifiedV1` cases. |
| **Department flow E2E** | ✅ Connected | Producer type string == consumer constant (`TenantDomainEvents.cs:125` == `DepartmentSoftDeleteConsumer.cs:18`); payload casing `DepartmentId` matches consumer extraction; exchange `security.events` topic; queue `authorization.department-soft-delete` quorum+DLQ; `role.Disable` + `SaveChangesAsync` (`DepartmentSoftDeleteConsumer.cs:35-65`). |
| **Outbox core pipeline** | ✅ Consistent | All 3 processors derive from shared `Platform...Outbox/OutboxProcessorBase.cs:15`; `OutboxPublishPolicy` MaxAttempts=10; `OutboxMessage`/`IPlatformOutboxRepository<T>` shared. |
| **MassTransit config parity** | ✅ Consistent (post MSG-003) | host + `PublisherConfirmation`, `SetEntityName(ExchangeName)`, durable topic publish, `Exponential(10,…)` retry, per-queue quorum+DLQ — present in all 3 services. |
| **Service boundaries** | ✅ Intact | No cross-service project references; no shared `DbContext`; integration only via HTTP clients + MassTransit envelopes. |
| **ADR-003 (OPA), ADR-004 (RLS), ADR-005 (5s decision cache, ALLOW-only), ADR-007-tenantid, ADR-009 (CachingBehavior + consumer), ADR-011 (Role dept immutability), ADR-012 (single RequestContext), ADR-013 (IResolveTenantInternally), ADR-015 (UnitOfWorkBehavior sole owner)** | ✅ Compliant | See §3. |
| **No code debt markers** | ✅ Verified | Grep of `src/**/*.cs` for `TODO|FIXME|HACK|XXX|NotImplementedException|NotImplemented|workaround|#if DEBUG` → 0 matches. |

---

## 3. Architecture Compliance

### ADR compliance matrix

| ADR | Verdict | Note |
|-----|---------|------|
| ADR-001 platform-architecture | ❌ Inconsistent | Names 4 services (Identity/Authorization/Policy/Audit); code has 3 (Identity/Tenant/Authorization). Omits TenantService; Policy/Audit not deployed as services. |
| ADR-002 event-driven-architecture | ⚠️ Drift | Lists `RoleUpdated`/`PermissionUpdated`/`PolicyUpdated`/`TenantUpdated` — none exist (grep 0). Actual: `RoleAssignedV1`, `PermissionGrantedV1`, `TenantCreatedV1`, … |
| ADR-003 policy-engine (OPA) | ✅ Compliant | `OpaPolicyEvaluationGateway`, `OpaDataUpdater`, `OpaSyncConsumer` present. |
| ADR-004 tenant-isolation (RLS) | ✅ Compliant | `TenantRlsInterceptor.cs:87-90` sets `SET LOCAL app.current_tenant_id` (+ department id); RLS policy in `RlsMigrationsSqlGenerator.cs:35`. |
| ADR-005 decision-cache | ✅ Compliant | `AuthorizationCacheOptions` 5s TTLs; ALLOW-only cached (`RedisAuthorizationCache.cs:75`). |
| ADR-006 kong-responsibilities | ❌ Not Implemented | No Kong/gateway artifact exists. |
| ADR-007-tenantid-standardization | ✅ Compliant | `TenantCode` VO; Authz `TenantId` VO removed. |
| ADR-007-department-membership-ownership | ⚠️ Proposed / collision | Duplicate ADR number; contradicts ADR-014 on Department-Membership ownership. |
| ADR-008-identity-context-redesign | ⚠️ Proposed (impl-ahead) | Session/User split + `AccessRisk` separate — present. |
| ADR-008-delegation-feature | ❌ Not Implemented / Drift | Accepted ADR with full plan; **no `Delegation` aggregate/controller/worker** exists (grep 0). |
| ADR-009 cache-invalidation | ✅ Compliant (3/5 svcs) | References Policy/Audit services that don't exist. |
| ADR-010 | ❌ Missing | Numbering gap — no ADR-010 document. |
| ADR-011 authorization-service-boundary | ✅ Compliant (1 known gap) | `RoleAssignment` still stores `DepartmentId` (`RoleAssignment.cs:24`) — ADR itself marks "to be removed". |
| ADR-012 request-context-platform-ownership | ✅ Compliant (declared) | Single `IRequestContextAccessor`/`RequestContext` in Platform — but **runtime registrations shadow it** (see §4/§6). |
| ADR-013 tenant-resolution-tenantid-ownership | ✅ Compliant | `IResolveTenantInternally` used; `CurrentPrincipalFactory` claim-only; `TenantBehavior` skips enforcement. |
| ADR-014 department-membership-ownership | ⚠️ Proposed + stale | Code is AHEAD: `User.DepartmentId` gone; `CurrentPrincipalFactory.cs:47-48` parses `department_id`/`role_id`. ADR "current state" is wrong. |
| ADR-015 unified-transaction-architecture | ✅ Compliant | `UnitOfWorkBehavior` sole owner; handlers pure. |
| ADR-016 caching-ownership-strategy | ⚠️ Proposed (refs non-existent svc) | Describes Policy/Audit caches. |
| ADR-017-messaging-architecture-audit | ❌ Stale (partial) | See §7. |
| ADR-017-outbox-audit | ✅ Accurate | Outbox capture/schema/dispatcher claims match code. |
| ADR-017-validation-report | ⚠️ Partially inaccurate | Correctly flags dead `IIntegrationEvent`/dead `*IntegrationEvent` records; wrong on dual-registration topology (see §7). |

**Duplicate/colliding ADR numbers** (documentation-integrity defect): two `ADR-007-*`, two `ADR-008-*`, three `ADR-017-*`. ADR-007-dept vs ADR-014 directly contradict each other on who owns Department Membership.

---

## 4. Remaining Issues

### High

- **H1 — Tenant RLS not scoped in background/consumer code (latent isolation gap).**
  `IRequestContextAccessor` is registered **Singleton** in all 3 services (`TenantService...ServiceCollectionExtensions.cs:59`, `AuthorizationService...:53-55`, `IdentityService...:108`) while depending on Scoped `IHttpContextAccessor`. In hosted services / MassTransit consumers / outbox dispatchers `HttpContext` is null → `TenantRlsInterceptor.BuildSetStatement` returns null (`TenantRlsInterceptor.cs:84-85`) → **no `SET LOCAL app.current_tenant_id`** applied. Currently masked because consumers re-scope via `envelope.TenantId` (e.g. `DepartmentSoftDeleteConsumer.cs:33,48` uses `IgnoreQueryFilters`), but any future background DB write that relies on the accessor runs unscoped. *Root cause: per-service `HttpRequestContext(Accessor)` singletons shadow the Platform scoped `RequestContextAccessor` (`Platform...Tenant/RequestContextAccessor.cs:5`), which is never the resolved instance.*
- **H2 — IdentityService omits shared platform bootstrap.** `AddPlatformMiddleware`, `AddPlatformAuthorizationPolicies`, `AddPlatformInfrastructure` are called by Tenant/Authorization but **not** IdentityService, which instead hand-registers primitives inline (`IdentityService...ServiceCollectionExtensions.cs:106-112`). Yet `Program.cs:113` calls `app.UsePlatformMiddleware(configuration)`. If `AddPlatformMiddleware` binds `MiddlewareOptions`/`RateLimitingOptions`, the middleware runs with all-false defaults; platform service-only authorization policies are also absent in IdentityService. *Verify in next phase.*
- **H3 — Hardcoded development signing key.** Default `"development-signing-key-change-in-secret-store-minimum-32-bytes"` in `Platform.Authorization/JwtOptions.cs:12`, `IdentityService...HmacJwtTokenGenerator.cs:24`, `AuthorizationService...ServiceCollectionExtensions.cs:141`, and literally in all three `appsettings.json` (`*Api/appsettings.json:46`). Secret Store not wired.

### Medium

- **M1 — Orphaned integration events cause stale caches.** Produced-but-unconsumed: `TenantNameUpdatedV1` (`Tenant.cs:165`), `DepartmentNameUpdatedV1` (`Department.cs:93`), `DepartmentDescriptionUpdatedV1` (`Department.cs:108`) → their `TenantCacheInvalidationConsumer`/`AuthorizationCacheInvalidationConsumer` `switch` has no case → tenant/department **name/description cache never invalidated**. Also `SessionCreatedV1`, `SessionRefreshTokenRotatedV1`, `UserSynchronizedV1` (no consumer).
- **M2 — `authorization.role-disabled.v1` not handled by authz cache invalidation.** Now produced live via `DepartmentSoftDeleteConsumer.cs:62` (`role.Disable`), handled by `OpaSyncConsumer`/`AuditPipelineConsumer`, but `AuthorizationCacheInvalidationConsumer.cs:33-64` has **no case** → authorization **decision cache not invalidated** when department deletion disables roles.
- **M3 — Duplicated magic-string event contracts.** Event type strings duplicated between producer and consumer with no shared constants (e.g. `TenantDomainEvents.cs:125` & `DepartmentSoftDeleteConsumer.cs:18`). Contract-ownership risk (roadmap #1).
- **M4 — Dead/duplicate outbox repository abstractions.** `ITenantOutboxRepository` (TenantService, never registered/injected) and `IOutboxRepository` (IdentityService, registered `:129` but never consumed) are shadowed by the live generic `IPlatformOutboxRepository<TDbContext>` + `OutboxRepository<TDbContext>`. Dead code paths.
- **M5 — Three unused per-service `DeadLetterMessage` classes.** `Platform...Outbox/DeadLetterMessage.cs:6` is canonical (used by `OutboxProcessorBase` + `*DeadLetterMessageConfiguration`); the Identity/Tenant/Authz `Persistence/DeadLetterMessage.cs:3` copies are never referenced.
- **M6 — `Platform.Abstractions.Caching.ICacheService` duplicates `SharedKernel.Caching.ICacheService`** and is never referenced (orphaned near-duplicate).
- **M7 — `AddPlatformAuthorization` never called.** Defined `Platform.Authorization/AuthorizationServiceCollectionExtensions.cs:12`; TenantService (`:151-185`) and IdentityService (`:187-221`) inline identical `IAuthorizationDecisionService` HttpClient + Polly wiring that has **drifted** (IdentityService adds a circuit-breaker TenantService lacks).
- **M8 — AuthorizationService uses a second Redis stack** (`AddStackExchangeRedisCache` + `IConnectionMultiplexer`, `DependencyInjection.cs:49,54`) bypassing the platform `IDistributedCacheService` used by the other two services — split caching strategy.
- **M9 — `JwtOptions` fragmentation.** Four types bound to `"Jwt"`: `Platform.Authorization.JwtOptions`, `IdentityService...HmacJwtTokenGenerator.JwtOptions`, `TenantService...JwtOptions`, `AuthorizationService...JwtOptions`. AuthorizationService binds **without** `ValidateDataAnnotations`/`ValidateOnStart` (fail-open); only TenantService has a `JwtOptionsValidator` (`ServiceCollectionExtensions.cs:83`).
- **M10 — Inconsistent JWT validation.** Only IdentityService enforces active-session check (`OnTokenValidated = ValidateSessionAsync`, `:89`); Tenant/Authorization accept JWT on claims alone.
- **M11 — `RequireHttpsMetadata = false` hardcoded** in all three JwtBearer setups (`:74`/`:34`/`:43`). Not env-driven.
- **M12 — Event naming convention split.** AuthorizationService uses `kebab-case.v1`; Tenant/Identity use `PascalCaseV1`. No shared contract assembly.
- **M13 — DbContext lifetime inconsistency.** AuthorizationService `AddDbContext` (`DependencyInjection.cs:59`) vs Tenant/Identity `AddDbContextPool`.
- **M14 — Service-token contract asymmetry.** `PlatformServiceTokenGenerator.cs:39-45` emits no `tenant_id`/`sub`/`session_id`; `IdentityService...ValidateSessionAsync` (`:282-287`) **fails** tokens missing `sub`/`session_id`. A service-to-service call to a user-protected IdentityService endpoint would be rejected. Doc `docs/E2E-Hardening-Report.fa.md:219` claims a `tenant_id` claim that is not emitted.

### Low

- **L1 — `IPlatformServiceRegistry` registered twice** in IdentityService (`:112` and `:225`).
- **L2 — Dead `OpaSyncConsumer` HashSet entries** `authorization.role-activated.v1`/`-deactivated.v1`/`-parent-changed.v1` (`:20-23`) — produced by `Role.Activate`/`Deactivate`/`SetParent`, none of which have callers.
- **L3 — ~12 dead domain events** declared but never raised (roadmap #10): e.g. `UserLoggedInDomainEvent` (`IdentityDomainEvents.cs:25-29`), `MfaDisabledV1`, `PasswordChangedV1`, `PasswordResetV1`, `authorization.role-activated/deactivated/parent-changed.v1`, `authorization.permission-deprecated/archived/version-created.v1`, `authorization.evaluated.v1` (no `RaiseDomainEvent` site), `authorization.usage-incremented.v1` (only via unused `UsageAccountingService`).
- **L4 — `UsageAccountingService` registered but never invoked** (`AuthorizationService...DependencyInjection.cs:107`).
- **L5 — `IOutboxProcessor` orphaned interface** (`Platform...Infrastructure/IOutboxProcessor.cs:3`) — never implemented/registered.
- **L6 — Three duplicate design-time `DbContextFactory` null accessors** and **hardcoded DB password `Developer1245`** in `Identity/Tenant/Authz DbContextFactory.cs:14-15` (design-time only).
- **L7 — Hardcoded localhost URLs/ports** as defaults (`AuthorizationServiceClientOptions.cs:7`, `RedisOptions.cs:7`, `OpaOptions.cs:7`, several `Program.cs` fallbacks).
- **L8 — TenantService hardcodes DLQ/DLX names** (`DependencyInjection.cs:132`) vs suffix constants used elsewhere; mixed dotted/hyphenated queue names across services.
- **L9 — `JwtWarmupTask` truncates key to 32 bytes** (`JwtWarmupTask.cs:31`) — exercises a different key than the runtime HMAC generators.
- **L10 — Old auto-named IdentityService queues orphaned** by MSG-003 rename to `identity.*` prefixes (no migration; in-flight messages/monitors referencing old names are orphaned — operational note).
- **L11 — `IRealTimeUsageStore.SaveQuotaSnapshotAsync` throws by default** (`IRealTimeUsageStore.cs:34`) — safe only because `RedisRealTimeUsageStore` overrides it.

---

## 5. Technical Debt

- Captive-dependency singleton `IRequestContextAccessor` (H1) — the single most important debt; it conflates request-scoped tenant state with a process-wide singleton and defeats RLS in background work.
- Fragmented `JwtOptions` / duplicated auth wiring (M7, M9) — should funnel through one Platform extension.
- Duplicated per-service `HttpRequestContext(Accessor)` (four implementations of one interface; Platform one dead) — consolidating onto the Platform scoped accessor (with proper `AsyncLocal`/background handling) resolves D1–D4, TP1–TP4, CP2, DC1–DC6 from the RequestContext review.
- Divergent caching stacks (M8) — AuthorizationService bypasses the platform cache abstraction.
- No in-code debt tracking (no TODO/FIXME markers) means debt is only discoverable by reading — a process debt in itself.

---

## 6. Dead Code

- `ITenantOutboxRepository` (TenantService) and `IOutboxRepository` (IdentityService) — registered/implemented, never injected (M4).
- Three per-service `DeadLetterMessage` classes — unused (M5).
- `Platform.Abstractions.Caching.ICacheService` — orphaned duplicate (M6).
- `AddPlatformAuthorization` extension — never called (M7).
- `IOutboxProcessor` — orphaned interface (L5).
- `AuthorizationService...UsageAccountingService` — registered, never invoked (L4).
- `UserLoggedInDomainEvent` and ~11 other declared-but-unraised domain events (L3); `AuthorizationEvaluatedDomainEvent` declared, never produced.
- `OpaSyncConsumer` dead HashSet entries (L2).
- `IIntegrationEvent` + 3 dead `*IntegrationEvent` records (flagged by ADR-017-outbox-audit) — never implemented/published.
- Old auto-named IdentityService queues (L10).
- Platform scoped `RequestContextAccessor` — effectively dead at runtime (shadowed) (H1/D1).

---

## 7. Documentation Drift

- **ADR-017-messaging-architecture-audit.md is stale (genuinely wrong on topology):** it claims a *second* `AddMassTransit` registration in `IdentityService.Infrastructure\DependencyInjection.cs` (a file that **does not exist** — only 3 `AddMassTransit` calls exist total) and queue names `identity.session.revocation`/`identity.tenant.cache` (0 matches). This is the one conflict that genuinely justifies the "stale" reclassification.
  - **Correction on the companion inconsistency report:** `docs/reports/INCONSISTENCY-REPORT-ADR017.md` Conflict B ("IdentityService has zero `SetQuorumQueue`/`BindDeadLetterQueue`") and Conflict C ("`DepartmentSoftDeletedV1` has no producer") described the **pre-remediation** state. They are now **RESOLVED** by MSG-003 and MSG-001/002 respectively. The report's premise for halting on B/C no longer holds against current code; only Conflict A (dual registration / non-existent file) remains valid.
- **RABBITMQ-ARCHITECTURE-REPORT.md is now stale on messaging state.** It still asserts `DepartmentSoftDeletedV1` has *no producer* and `DepartmentSoftDeleteConsumer` is *dead* (multiple sections), and that IdentityService lacks quorum/DLQ. All three are now false post-remediation (F11). This will mislead future work.
- **PHASE-1-IMPLEMENTATION-REPORT.md** correctly states `UserLoggedInDomainEvent` was left in place "out of scope" — consistent with L3.
- **ADR-002** references non-existent event names; **ADR-001/ADR-006/ADR-008-delegation/ADR-009/ADR-016** reference non-deployed Policy/Audit services.
- **ADR-014** "current state" section contradicts code (`User.DepartmentId` removed; `CurrentPrincipalFactory` parses `department_id`).
- **Duplicate/colliding ADR numbers** (007×2, 008×2, 017×3) and **missing ADR-010** — documentation-integrity defect.
- `docs/E2E-Hardening-Report.fa.md:219` claims a `tenant_id` service-token claim that `PlatformServiceTokenGenerator` does not emit (M14).

---

## 8. Production Risks

| Risk | Severity | Impact |
|------|----------|--------|
| Background/consumer DB work runs with **no RLS tenant scoping** (H1) | High | Cross-tenant data exposure/modification if a future consumer relies on the accessor instead of explicit `envelope.TenantId` scoping. |
| Hardcoded dev signing key in code + all `appsettings.json` (H3) | High | Token forgery if a non-prod key leaks; no Secret Store. |
| IdentityService runs platform middleware/policies with defaults (H2) | High | Rate-limiting / authorization policies may be inactive or misconfigured in IdentityService. |
| Stale tenant/department name+description caches (M1) | Medium | Authorization/cache serves outdated names after rename. |
| Decision cache not invalidated on role-disable (M2) | Medium | Subjects retain stale authorization decisions after department deletion disables roles. |
| `RequireHttpsMetadata=false` hardcoded (M11) | Medium | Tokens accepted over plain HTTP in any environment. |
| Service-token rejected by IdentityService session validation (M14) | Medium | Service-to-service calls to user-protected IdentityService endpoints fail. |
| Dual caching stacks (M8) | Medium | Inconsistent cache behavior/observability across services. |
| Orphaned queues / no DLQ migration (L10) | Low | Orphaned messages on deploy; no replay path documented. |

---

## 9. Recommended Fixes (prioritized)

**P0 — before any production deployment (addresses High):**
1. **H1:** Consolidate tenant context onto the Platform scoped `RequestContextAccessor` with `AsyncLocal`-backed state so background/consumer/outbox code is correctly tenant-scoped; remove the three per-service `HttpRequestContext(Accessor)` singletons. (Resolves H1, D1–D4, TP1–TP4, CP2, DC1–DC6.)
2. **H3:** Wire a real secret store for `Jwt:SigningKey`; remove the hardcoded default and the literal keys from `appsettings.json` (keep only env override).
3. **H2:** Make IdentityService call `AddPlatformMiddleware` / `AddPlatformAuthorizationPolicies` / `AddPlatformInfrastructure` (or confirm `UsePlatformMiddleware` binds options independently) so middleware + policies are active and consistent.

**P1 — next remediation phase (Medium):**
4. **M1:** Add consumer cases for `TenantNameUpdatedV1` / `DepartmentNameUpdatedV1` / `DepartmentDescriptionUpdatedV1` (or stop publishing) to invalidate caches.
5. **M2:** Add an `authorization.role-disabled.v1` case to `AuthorizationCacheInvalidationConsumer` to invalidate the decision cache.
6. **M4/M5/M6:** Delete dead outbox repos (`ITenantOutboxRepository`, Identity `IOutboxRepository`), the three per-service `DeadLetterMessage` classes, and the orphan `Platform.Abstractions.Caching.ICacheService`; rely on Platform generics.
7. **M7:** Call `AddPlatformAuthorization` in Tenant/Identity (remove inlined drifted wiring).
8. **M8:** Route AuthorizationService through `AddPlatformCaching` (drop the separate `IDistributedCache` stack) or document the deliberate split.
9. **M9/M10/M11:** Single `JwtOptions` type with startup validation everywhere; consistent session validation; env-driven `RequireHttpsMetadata`.
10. **M14:** Reconcile service-token claims with `ValidateSessionAsync` (either emit `sub`/`session_id` for service calls or exempt service tokens from session validation).
11. **M12:** Pick one event-naming convention (roadmap #9) — defer to the Event Contracts ADR.

**P2 — backlog / cleanup (Low):**
12. **L1:** De-duplicate `IPlatformServiceRegistry` registration. **L3/L5:** Remove dead domain events / `IOutboxProcessor` / `UsageAccountingService` (roadmap #10). **L2:** Remove dead `OpaSyncConsumer` HashSet entries. **L6/L7:** Externalize DB password + localhost defaults to config/secret store. **L9:** Fix `JwtWarmupTask` key handling. **L11:** Make `SaveQuotaSnapshotAsync` not throw by default or seal the interface.
13. **Documentation (§7):** Mark ADR-017 stale precisely (only Conflict A valid); update RABBITMQ-ARCHITECTURE-REPORT.md messaging-state sections; fix ADR-002/001/006/008-delegation/009/014 drift and duplicate ADR numbering; correct the service-token claim doc.

---

## 10. GO / NO-GO

- **Phase 1 messaging remediation (MSG-001..004):** ✅ **GO.** Complete, correct, internally consistent, verified end-to-end, builds with 0 errors, 2 unit tests pass. No blockers to proceed.
- **Platform production readiness:** ⛔ **NO-GO** until P0 items (H1 tenant-RLS in background, H3 hardcoded signing key, H2 IdentityService platform bootstrap) are remediated. These are pre-existing and outside Phase 1 scope, but they are production-blocking for a security platform.
- **Next phase:** **GO** to begin remediation of P0 → P1 items. The recommended fixes in §9 are scoped, decision-free (except M12 naming and the Event Contracts ADR), and do not redesign the frozen architecture.

> No code was modified during this audit. The findings above are for review; whether any fix is applied before the next phase is a decision for the user.
