# PolicyService — Production Readiness Review

- **Date:** 2026-07-15
- **Author:** Enterprise Architecture Reviewer (read-only verification pass)
- **Scope:** Full production-readiness audit of `PolicyService` (Domain, Application, Infrastructure, API, Integration, Hosted Services, MassTransit, Outbox, Cache, Recovery Jobs, OPA Synchronization, Tenant Isolation) against ADR-012, ADR-013, ADR-014, ADR-015, ADR-017, ADR-018.
- **Method:** Static read-only review of the solution (no code, config, or docs modified). Every finding is evidence-based with `file:line`. Cross-checked against the platform pipeline behaviors (`Platform.Behaviors`), the shared outbox/RLS base (`Platform.Infrastructure`), and the prior IdentityService/AuthorizationService/TenantService reviews. `dotnet build` executed (0 errors) as a baseline compile check.
- **Baseline:** `docs/adr/ADR-012..018`, `docs/reports/PLATFORM-IMPLEMENTATION-AUDIT.md`, `docs/reports/PRODUCTION-HARDENING-REPORT.md`, `src/Platform/**`, `src/Shared/**`, `src/Services/PolicyService/**`.

> **Note on prior reviews:** the earlier IdentityService/AuthorizationService/TenantService reviews used a NO-GO/GO + High/Medium/Low vocabulary. This review adopts the requested BLOCKER / HIGH / MEDIUM / LOW classification. A release **BLOCKER** is any defect that prevents a correct, safe production deployment; **HIGH** is production-blocking security/config debt; **MEDIUM** is correctness/performance/compliance debt to fix before or shortly after launch; **LOW** is cleanup.

---

## 1. Executive Summary

`PolicyService` is **architecturally well-formed and compiles cleanly (0 errors)**. The domain model correctly implements the ADR-018 debt/recovery semantics (debt-first recovery, never-negative debt, monthly debt dominance, reset-clears-usage+debt-not-quota, consumption never truncates — it debts the excess). Optimistic concurrency is correctly implemented (`Entity.MarkUpdated()` bumps `Version`, which is an `IsConcurrencyToken`). The platform pipeline (`UnitOfWorkBehavior`, `IdempotencyBehavior` via distributed Redis store, `RetryBehavior` with `DbUpdateConcurrencyException` handling) is solid, and the MassTransit configuration (exponential retry ×10, quorum queues, DLQ, durable topic exchange, single `AddMassTransit`) is consistent with the other services (ADR-017 compliant on topology).

However, the service is **NOT production-ready**. Four BLOCKER-class defects make it non-functional and/or unsafe on deployment:

1. **B1 — Authorization is silently fail-open.** `IAuthorizationDecisionService` is never registered, so `AuthorizationBehavior` skips enforcement and any authenticated tenant principal can `PublishPolicy` (→ OPA sync → platform-wide authorization impact) and `ResetAllowance`.
2. **B2 — No HTTP API surface is mapped.** `Program.cs` exposes only `/` and health; the entire Application layer is unreachable over HTTP.
3. **B3 — No EF Core migrations exist.** `ApplyMigrationsAsync` produces no schema; every persistence call fails.
4. **B4 — RLS is applied to `policy_outbox` / `policy_dead_letter`.** The outbox dispatcher runs without a tenant context; RLS filters it to zero rows, so **no integration event is ever published** (OPA sync, cache invalidation, and principal-hierarchy hydration are all dead).

Two HIGH secrets-hygiene defects (hardcoded JWT signing key and committed DB/RabbitMQ credentials in `appsettings.json`) remain — the other three services had these removed.

**Verdict: NO-GO.** Do not deploy until B1–B4 are remediated and H1–H2 are resolved. Detail in §4–§9.

---

## 2. What Is Verified Good

| Area | Status | Evidence |
|------|--------|----------|
| Compile | ✅ | `dotnet build PolicyService.Api.csproj -c Debug` → 0 errors, 3 warnings |
| Optimistic concurrency | ✅ | `Entity.MarkUpdated()` increments `Version` (`SharedKernel/.../Entity.cs:42`); `IsConcurrencyToken()` on every aggregate config; `SaveChangesAsync` writes the bumped version |
| Outbox capture atomicity | ✅ | `PolicyDbContext.SaveChangesAsync` captures `AggregateRoot.DomainEvents` into `OutboxMessages` inside the same `base.SaveChangesAsync` (`PolicyDbContext.cs:42-58`) |
| Transaction ownership (design) | ✅ | `UnitOfWorkBehavior` is sole owner; `Behaviors:EnableUnitOfWork=true`; handlers do not call `BeginTransaction` |
| Idempotency (hot path) | ✅ | `IdempotencyBehavior` + `DistributedIdempotencyStore` (Redis); `RecordConsumption` is `IIdempotentRequest` with caller-supplied `IdempotencyKey` |
| Concurrency retry | ✅ | `RetryBehavior` retries on `DbUpdateConcurrencyException` (exponential + jitter) |
| Messaging topology | ✅ | Single `AddMassTransit`; `Exponential(10,1s,30s,5s)`; quorum + DLQ on all 3 endpoints; durable `policy.events` topic (`ServiceCollectionExtensions.cs:54-92`) |
| Consumer tenant scoping | ✅ | `PrincipalHierarchyConsumer`/`OpaSyncConsumer` set `requestContextAccessor.Context` from `envelope.TenantId` before dispatch |
| Consumer idempotency | ✅ | `IEventConsumerDeduplicationGuard` (7-day TTL) on all 3 consumers |
| ADR-018 debt math | ✅ | `DebtLedger.Recover` does `Available = RenewedAllowance − OutstandingDebt`; debt never negative; `OutstandingDebt` only reduced by recovery; monthly dominance enforced in `AllowanceEngine.Consume` |
| ADR-018 reset semantics | ✅ | `ResetAllowanceCommandHandler` clears usage + debt only; no `IQuotaPolicyRepository` dependency (`ResetAllowance.cs:33`) |
| ADR-018 no-truncate | ✅ | `AllowanceEngine.Consume` debts the excess (`count − limit`) rather than truncating the operation |
| OPA compile-before-publish | ✅ | `PublishPolicyCommandHandler` calls `regoGenerator.Generate(...)` → `SetCompiledRego` → `Publish` (`PublishPolicy.cs:36-52`) |
| Tenant RLS mechanism | ✅ | `TenantRlsInterceptor` (`SET LOCAL app.current_tenant_id`) + global tenant/soft-delete query filter on all `Entity` types (`PolicyDbContext.cs:60-87`) |
| No code-debt markers | ✅ | grep `TODO|FIXME|HACK|XXX|NotImplementedException|#if DEBUG` → 0 matches in `src/Services/PolicyService` |

---

## 3. Architecture Compliance Matrix (ADR-012/013/014/015/017/018)

| ADR | Verdict | Note |
|-----|---------|------|
| **ADR-012** — Request-Context Platform Ownership | ✅ Compliant | Single Platform `IRequestContextAccessor` (AsyncLocal). Domain events carry only `TenantId`+`CorrelationId` (no `RequestContext`); `PolicyDomainEvent` comment enforces this (`PolicyDomainEvents.cs:9-14`). No per-service duplicate accessor. |
| **ADR-013** — Tenant Resolution / TenantId Ownership | ✅ Compliant | JWT → middleware → `RequestContext`; consumers propagate `envelope.TenantId` only (no re-resolution); RLS + global filter enforce; no `TenantId` accepted from headers. |
| **ADR-014** — Department Membership Ownership | ✅ Compliant (N/A) | PolicyService does not own departments; references principals by id only. Principal-hierarchy consumer parses **role** events (authz-owned) — correct ownership boundary. |
| **ADR-015** — Unified Transaction Architecture | ⚠️ Design compliant, blocked | `UnitOfWorkBehavior` sole owner; outbox captured atomically; handlers pure. **Blocked by B3 (no migrations → no schema) and B4 (outbox RLS-locked).** |
| **ADR-017** — Messaging / Outbox Audit | ❌ Violation | Outbox *pattern* correct (capture/dispatch/DLQ/lease), but **B4** RLS-locks `policy_outbox` → dispatcher affects 0 rows → never dispatches. Also M3 (fragile payload parsing). |
| **ADR-018** — Policy/Quota/Debt/Recovery Domain | ⚠️ Partial | Debt math, reset, no-truncate, dominance all correct. **Gaps:** recovery only covers **Monthly** window (M2); Role-scope resolution deferred (M4); principal-hierarchy read model incompletely hydrated (M8). |

---

## 4. BLOCKER Findings (release must not proceed)

### B1 — Authorization enforcement is fail-open (privileged commands unauthorized)
`PolicyService.Api/Extensions/ServiceCollectionExtensions.cs:49` calls `services.AddPlatformAuthorizationPolicies()` only. That extension registers ASP.NET **claim policies** (`Platform.Middleware/AuthorizationPolicyExtensions.cs`) — it does **not** register `IAuthorizationDecisionService`. The runtime decision client is registered exclusively by `AddPlatformAuthorization(configuration)` (`Platform.Authorization/AuthorizationServiceCollectionExtensions.cs:12`), which PolicyService never calls.

Consequence: in `Platform.Behaviors/AuthorizationBehavior.cs:34`:
```csharp
var authService = serviceProvider.GetService<IAuthorizationDecisionService>();
if (authService is null)
    return await next();   // <-- fail-open: enforcement skipped
```
`FailClosedBehavior` only converts *exceptions during `next()`* into `ServiceUnavailable`; it does **not** cover this skip. Therefore `PublishPolicyCommand` (`Action="policy.publish"`, `PublishPolicy.cs:14`) and `ResetAllowanceCommand` (`Action="allowance.reset"`, `ResetAllowance.cs:18`) — both `IAuthorizableRequest` — execute **without any authorization check**. Any authenticated tenant principal can publish policies (which sync to OPA and alter platform-wide authorization decisions) and reset any consumer's allowance.

This is the same root cause as the prior review's M7 (`AddPlatformAuthorization` never called), but in PolicyService it is not mere drift — it is a **complete absence**, producing fail-open authorization. **Severity: BLOCKER (security).**

### B2 — No HTTP API endpoints are mapped
`Program.cs:110` maps only `app.MapGet("/", () => "PolicyService API")` plus health. There are **no controllers, no Minimal API `MapGroup`/`MapPost`/`MapGet`, and no FastEndpoints/Carter** anywhere in `src/Services/PolicyService` (grep for `MapGroup|MapPost|MapGet|IEndpointRouteBuilder|Controller|FastEndpoints` → 0). The `PolicyService.Api.http` file is still the default `weatherforecast` template. The entire Application layer — `CreatePolicy`, `PublishPolicy`, `RecordConsumption`, `DefineQuotaPolicy`, `ResolveEffectiveQuotaQuery`, `ResetAllowance`, `RecoverDebt`, etc. — is unreachable over HTTP.

For a project named `PolicyService.Api` that the feed and administrators must call, this is a release blocker. (If PolicyService is intentionally event/OPA-only with no HTTP contract, this must be explicitly confirmed and the dead Application handlers removed — but the presence of an `Api` host plus admin/feed handlers indicates HTTP exposure is expected.) **Severity: BLOCKER.** Confirm intent; if HTTP is required, blocking.

### B3 — No EF Core migrations exist (no database schema)
`src/Services/PolicyService/PolicyService.Infrastructure/Migrations/` is empty; no `MigrationAttribute`, no model snapshot (`find ... -path "*Migrations*"` → none; grep for `MigrationAttribute|__EFMigrationsHistory` → none). `Program.cs:103` calls `await app.ApplyMigrationsAsync<PolicyDbContext>()`. With zero migrations, EF creates only the `__EFMigrationsHistory` table and **no domain tables** — the service starts but every query/command fails (`relation "policies" does not exist`, etc.).

Every other service ships an initial migration. PolicyService ships none. **Severity: BLOCKER** (service cannot persist anything). The initial migration must be generated *after* B4 is fixed (so the generated migration excludes RLS on the outbox tables).

### B4 — RLS applied to `policy_outbox` / `policy_dead_letter` (outbox never dispatches)
`Platform.Infrastructure/Persistence/Migrations/RlsMigrationsSqlGenerator.cs:40-48` (`IsSystemOutboxTable`) excludes only `tenant_outbox/tenant_dead_letter`, `identity_outbox/identity_dead_letter`, `authorization_outbox/authorization_dead_letter`. It does **not** exclude `policy_outbox` / `policy_dead_letter`. The other three services had to add `DropOutboxRls` migrations (e.g. `AuthorizationService.Infrastructure/Migrations/20260714145628_DropOutboxRls.cs`) precisely because the outbox dispatcher runs **without a tenant context** and RLS (`SET LOCAL app.current_tenant_id`) would filter it to zero rows.

In PolicyService the dispatcher (`Platform.Infrastructure/Outbox/OutboxProcessorBase.cs`) never sets `requestContextAccessor.Context`; `TenantRlsInterceptor.BuildSetStatement` returns `null` when `TenantId == Guid.Empty` (`TenantRlsInterceptor.cs`). So `ClaimPendingBatchAsync` (raw `UPDATE ... FOR UPDATE SKIP LOCKED` in `Platform.Infrastructure/Outbox/OutboxRepository.cs`), `MarkProcessedAsync`, `MarkFailedAsync`, and `MoveToDeadLetterAsync` all execute under RLS with `app.current_tenant_id` unset → **0 rows affected** → the outbox **never dispatches**. All integration events (`policy.policy-published.v1` → OPA sync, the `policy.*` cache-invalidation events, `authorization.role-*` → principal-hierarchy hydration) are never published.

**Severity: BLOCKER.** Fix: add `policy_outbox`/`policy_dead_letter` to `IsSystemOutboxTable` (shared generator — correct, future-proof fix) and/or generate a `DropOutboxRls`-equivalent migration for PolicyService. Must be done **before** generating the B3 initial migration.

---

## 5. HIGH Findings

### H1 — Hardcoded JWT signing key committed in `appsettings.json`
`PolicyService.Api/appsettings.json` → `"Jwt": { "SigningKey": "change-me-in-production-policyservice-signing-key" }`. Because the key is non-empty, it satisfies `[Required]` + `ValidateOnStart` (`ServiceCollectionExtensions.cs:31-39`), so production would sign/verify JWTs with a **publicly known** key unless overridden by environment/secret store → token forgery.

The other three services had this exact defect removed during the platform hardening pass (H3) and now fail fast on an empty key sourced from a secret store. PolicyService regressed on this control. **Severity: HIGH** (production-blocking security for a security platform; treat as a release blocker).

### H2 — Database & RabbitMQ credentials committed in `appsettings.json`
`appsettings.json` → `"ConnectionStrings": { "Policy": "...Password=postgres" }` and `"RabbitMq": { "Username": "guest", "Password": "guest" }`. Runtime secrets are in source and would be used in any environment that does not override them. These must be externalized to a secret store / environment. **Severity: HIGH** (secret hygiene; same class as prior L6/L7, but it is live runtime config here).

---

## 6. MEDIUM Findings

### M1 — Missing indexes on hot-path resolution queries
`QuotaResolver.ResolveAsync` and `SubscriptionResolver.ResolveAsync` call `GetByScopeAsync(tenantId, scope)` on **every consumption** (`QuotaResolver.cs:18-26`, `SubscriptionResolver.cs:20-22`; `QuotaPolicyRepository.cs:16-17` → `WHERE TenantId == tenantId && Scope == scope`). Neither `QuotaPolicyConfiguration` nor `SubscriptionConfiguration` defines an index on `Scope`; the PK is `(TenantId, Id)`. Within a tenant partition this is a sequential scan. `PrincipalEdgeConfiguration` defines only the `(TenantId, UserId, RoleId)` PK and a redundant identical unique index — no standalone `(TenantId, UserId)` / `(TenantId, RoleId)` index for hierarchy traversal. **Severity: MEDIUM (performance).** Add `HasIndex(p => new { p.TenantId, p.Scope })` on `QuotaPolicy`/`Subscription`, and edge traversal indexes.

### M2 — Recovery covers only the Monthly window
`RecoveryProcessor` repays the **single** `OutstandingDebt(action)` by the renewed allowance of whichever window's boundary elapsed (lazy first-access, no scheduler). `DebtLedger.Recover` acts on the single debt and records a per-window `RecoveryPosition`. Recovery is triggered by `AllowanceEngine.Consume` for any Daily/Weekly/Monthly window that has rolled over since the last recovery, inside the same `UnitOfWorkBehavior` transaction (ADR-015). There is no per-window debt bucket, so the prior "Daily/Weekly debt never auto-recovered" finding (M2) does not apply to the corrected model. **Correctness: RESOLVED by the approved ADR-018 amendment (`02-debt-recovery-design-review.md`).**

### M3 — Fragile cross-service event-contract coupling
Consumers parse producer event payloads by property name / VO shape with no shared versioned DTO:
- `PrincipalHierarchyConsumer.cs:78-92` reads `envelope.Payload.SubjectIdValue` / `RoleId` from AuthorizationService events.
- `OpaSyncConsumer.cs` `TryGetPolicyId` reads `envelope.Payload.PolicyId.Value` (depends on `PolicyId` VO serializing as `{"Value":"guid"}`).

A rename in a producer breaks PolicyService consumers **silently** (no compile error, payload simply missing → event skipped or ledger not created). **Severity: MEDIUM (correctness/contract).** Mitigated somewhat by dedup, but there is no schema validation. Recommend a shared event-contract assembly / explicit DTOs (ADR-017 contract ownership).

### M4 — Role-scope quota/subscription resolution deferred
`RecordConsumption.cs` builds `candidateScopes = [User, Tenant]` only; the Role scope is explicitly deferred ("until the read model is hydrated (P4/UC-26-29)"). ADR-018 resolution order is User → Role → Tenant; Role-level overrides are not yet honored. **Severity: MEDIUM (known gap, documented in code).** Track to the principal-hierarchy hydration completion.

### M5 — Tenant↔consumer ownership not verified on commands
`RecordConsumption`/`RecoverDebt` use the request/command `TenantId` together with a caller-supplied `ConsumerId` without verifying the consumer belongs to that tenant (`RecordConsumption.cs`, `RecoverDebt.cs`). A caller could attribute usage/debt to a `(tenantId, consumerId)` pair it does not own (the ledger is keyed by that pair). Latent until B2 endpoints exist, but there is no ownership gate even then. **Severity: MEDIUM (tenant isolation/correctness).**

### M6 — Cache key tenant-scoping not independently verified
`CachingBehavior` (enabled) caches queries; `CacheInvalidationConsumer.cs` invalidates by the pattern `*Tenant:{envelope.TenantId:N}*`. The write-path cache keys must include the tenant to avoid cross-tenant cache leakage. Not verifiable from PolicyService alone — confirm in `Platform.Caching`/`CachingBehavior`. **Severity: MEDIUM (verify).**

### M7 — Audit behavior disabled
`appsettings.json` → `"Behaviors": { "EnableAudit": false }`. ADR-018 marks debt/recovery/reset as audit-relevant, yet `AuditBehavior` is off → no audit trail for policy publish/archive, quota changes, or allowance resets. **Severity: MEDIUM (compliance).**

### M8 — Principal-hierarchy read model incompletely hydrated
`PrincipalHierarchyConsumer.cs` `HandledEvents` = `role-created` / `role-assigned` / `role-revoked` only (comment: `UserCreated`/`UserTenantChanged` not yet emitted by any service). Consequently the `UpsertUserNode` branch in `UpsertPrincipalEdgeCommandHandler` is **never triggered by any event** — user nodes and per-user `principal_nodes` rows are never created via the event path (ledgers are created lazily by `GetOrCreateAsync` on first consumption, but the hierarchy graph lacks user nodes). This blocks Role-scope resolution (M4). **Severity: MEDIUM (correctness/known gap).**

### M9 — Idempotency depends on Redis availability
`DistributedIdempotencyStore` (Redis) backs `IdempotencyBehavior`. If Redis is unavailable, `TryRegisterAsync` fails and requests can be blocked/rejected. Acceptable for single-instance but a resilience dependency for multi-instance; confirm circuit-breaking/fallback. **Severity: MEDIUM (verify).**

### M10 — OPA synchronization can diverge from the outbox
`OpaSyncConsumer` does `httpClient.PutAsJsonAsync("/v1/data/...", data)` and `EnsureSuccessStatusCode()` (`OpaDataUpdater.cs`). On OPA downtime the message retries (MassTransit ×10) then DLQs — but the **outbox message was already marked processed** by the dispatcher, so OPA and the outbox can permanently diverge (stale/missing OPA policy) with no automated DLQ replay. For a security policy engine this is an eventual-consistency risk. **Severity: MEDIUM.**

---

## 7. LOW Findings

- **L1 — Dead `IMessagePublisher` / `RabbitMqMessagePublisher`.** Registered as singleton (`DependencyInjection.cs:51`) but never injected anywhere (grep → only the registration + its own definition). The outbox pipeline is the sole publish path. Remove or document.
- **L2 — Code smells (build warnings).** `UpsertPrincipalEdgeCommand.cs:53` unused `requestContext` injection (CS9113); `DependencyInjection.cs:14` duplicate `using Platform.Infrastructure.Outbox` (CS0105); `RlsMigrationsSqlGenerator.cs:11` uses internal Npgsql API (EF1001 warning — stability risk on Npgsql upgrade).
- **L3 — Recovery is lazy first-access.** Recovery fires inside `AllowanceEngine.Consume` when a window boundary elapses; no periodic full-scan job exists. O(debtors) per cycle no longer applies. (Former M2/L3 concern resolved by removing the scheduler.)
- **L4 — `ListDebtorsAsync` eager-loads full `Debts` collection** for every debtor each cycle (`.Include(l => l.Debts)`) though only `Amount > 0` is needed.
- **L5 — `PrincipalEdgeConfiguration` redundant index** identical to the composite PK.

---

## 8. Production Risks

| Risk | Severity | Impact |
|------|----------|--------|
| Fail-open authorization (B1) | BLOCKER | Any tenant user can publish policies (→ OPA → platform-wide authz change) and reset allowances |
| No HTTP surface (B2) | BLOCKER | Service cannot receive admin/feed requests; Application layer dead |
| No schema (B3) | BLOCKER | All persistence fails at runtime |
| Outbox RLS-locked (B4) | BLOCKER | No integration events published; OPA/cache/hierarchy all stale |
| Hardcoded signing key (H1) | HIGH | JWT forgery if not overridden in prod |
| Committed credentials (H2) | HIGH | Secret leakage |
| Recovery only Monthly (M2) | MEDIUM | Daily/Weekly debt never repaid → permanent debt for those windows |
| Event-contract coupling (M3) | MEDIUM | Silent consumer breakage on producer rename |
| OPA/outbox divergence (M10) | MEDIUM | Stale OPA policy after DLQ with no replay |
| Missing resolution indexes (M1) | MEDIUM | Sequential scans on every consumption at scale |

---

## 9. GO / NO-GO

**NO-GO.** PolicyService must not be deployed until **B1–B4** are remediated (authorization wired, HTTP endpoints mapped, migrations generated with outbox RLS excluded, outbox dispatch verified) and **H1–H2** (secrets externalized) are resolved. MEDIUM items (M1–M10) should be scheduled before or immediately after launch; LOW items are cleanup.

The underlying architecture (domain model, transaction/outbox design, pipeline, messaging topology, tenant-isolation mechanism) is sound — the blockers are integration/wiring and release-hygiene gaps, not fundamental design flaws. Once B1–B4 + H1–H2 are fixed and a green end-to-end smoke test (publish policy → outbox → OPA sync; record consumption → debt → recovery) passes, the service is a GO.

> No code was modified during this audit. Findings are for review; implementation begins only after the remediation plan (`01-remediation-plan.md`) is approved.
