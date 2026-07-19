# PolicyService — Remediation Plan

- **Date:** 2026-07-15
- **Author:** Enterprise Architecture Reviewer
- **Companion:** `00-production-readiness.md` (findings B1–B4, H1–H2, M1–M10, L1–L5)
- **Status:** Draft — **requires approval before any implementation begins.** Do not start coding until the plan is signed off.

This plan is ordered by dependency and risk. **Phase 0 (BLOCKERs) is mandatory for any production deployment.** Phases 1–3 are HIGH/MEDIUM and should land before or shortly after launch. Phase 4 is cleanup.

Dependency note: **B4 must be fixed before B3** — the initial migration (B3) must be generated *after* `policy_outbox`/`policy_dead_letter` are excluded from RLS, otherwise the generated migration re-introduces the RLS-lock.

---

## Phase 0 — BLOCKER remediation (pre-deployment, mandatory)

### P0-1 — Wire authorization decision service (B1)
- **Fix:** In `PolicyService.Api/Extensions/ServiceCollectionExtensions.cs`, replace/augment `AddPlatformAuthorizationPolicies()` with `AddPlatformAuthorization(configuration)` (registers `IAuthorizationDecisionService` HttpClient + Polly to AuthorizationService). Add `AuthorizationServiceClientOptions` (`BaseUrl`, etc.) to `appsettings.json` + secret store. Keep `AddPlatformAuthorizationPolicies()` (ASP.NET policies still needed).
- **Why:** `AuthorizationBehavior` currently returns `next()` when `authService is null` → fail-open. After this, `PublishPolicy` (`policy.publish`) and `ResetAllowance` (`allowance.reset`) are enforced; `FailClosedBehavior` converts decision-service outages into `ServiceUnavailable` (safe).
- **Verify:** unit/integration test that an unprivileged tenant user receives `403 Forbidden` on `PublishPolicy`/`ResetAllowance`; a service principal (or authorized role) succeeds. Grep confirms `IAuthorizationDecisionService` is resolvable in the PolicyService DI container.
- **Decision:** none (deterministic fix).

### P0-2 — Map the HTTP API surface (B2)
- **Fix:** Expose the Application handlers via Minimal API endpoint groups (or controllers/FastEndpoints) in `Program.cs` (or an `app.MapPolicyEndpoints()` extension). Map at least: policies (create/publish/archive/change-condition/get/list), quota policies (define/amend/remove/get/list/resolve), subscriptions (assign/activate/revoke/supersede/get/list/resolve), consumption (record), ledgers (usage/debt queries), administration (reset), recovery (recover), principal-hierarchy (upsert). Apply `[Authorize]`/policies consistent with the platform.
- **Why:** Without endpoints the Application layer is unreachable; the feed and admins cannot call the service.
- **Verify:** `PolicyService.Api.http` exercises each endpoint; a smoke test posts a policy and publishes it.
- **Decision (confirm with user):** Is PolicyService intended to be HTTP-exposed, or is it purely event/OPA-driven? If purely event-driven, instead *remove* the now-dead Application handlers and the `Api` host's expectation of HTTP. **Default assumption: HTTP is required.** Do not implement until this is confirmed.

### P0-3 — Exclude outbox tables from RLS (B4) — do this BEFORE P0-4
- **Fix:** In `Platform.Infrastructure/Persistence/Migrations/RlsMigrationsSqlGenerator.cs:40-48`, add `"policy_outbox"` and `"policy_dead_letter"` to `IsSystemOutboxTable` (shared generator; corrects all future services). Optionally also add a `DropOutboxRls`-style migration for PolicyService mirroring the other three services.
- **Why:** The outbox dispatcher runs without a tenant context; RLS filters it to 0 rows → outbox never dispatches.
- **Verify:** After migration, confirm `policy_outbox`/`policy_dead_letter` have **no** RLS policy (`SELECT relname FROM pg_policy ...` / `pg_class.relrowsecurity = false`); and that `OutboxProcessor` publishes a test event end-to-end (event appears in RabbitMQ; OPA/cache/hierarchy consumers receive it).

### P0-4 — Generate and commit EF migrations (B3) — AFTER P0-3
- **Fix:** `dotnet ef migrations add InitialCreate` for `PolicyDbContext` (with the RLS generator now excluding outbox tables). Commit the migration + snapshot.
- **Why:** `ApplyMigrationsAsync` currently creates no schema.
- **Verify:** Fresh DB + `ApplyMigrationsAsync` creates all tables (`policies`, `subscriptions`, `quota_policies`, `usage_ledgers`, `debt_ledgers`, `principal_nodes`, `principal_edges`, `policy_outbox`, `policy_dead_letter`); integration test persists + reads a `Policy`.

### P0-5 — Externalize secrets (H1, H2)
- **Fix:** Remove `Jwt:SigningKey` literal and `ConnectionStrings.Policy`/`RabbitMq` credentials from `appsettings.json`; source from secret store / environment. Keep dev-only values in `appsettings.Development.json`. (Mirror the platform hardening H3/L6/L7 treatment applied to the other services.)
- **Why:** Publicly-known signing key → JWT forgery; committed DB/RabbitMQ creds → leakage.
- **Verify:** Prod config with empty/omitted secret fails fast (`ValidateOnStart` + `[Required]` on `JwtOptions`); service boots only when the secret is supplied via env/secret store.

---

## Phase 1 — HIGH secrets & correctness (before launch)

### P1-1 — Debt is a single (action) liability, recovered by ANY renewal (M2, corrected by `02-debt-recovery-design-review.md`)
- **Fix (superseded):** The M2 finding assumed per-window debt (Daily/Weekly/Monthly buckets) recovered by a scheduled `RecoveryProcessorJob`. The authoritative business rule — and the approved ADR-018 amendment — state debt is a **single outstanding liability per (consumer, action)**, NOT window-scoped, and is repaid **debt-first by any window's renewal**. The implementation now stores debt in `DebtLedger.Debts` keyed per action (no window), `DebtEntry` has no `Window`, and `AllowanceEngine.Consume` triggers recovery **lazily** (before each consumption, for each window whose boundary elapsed) inside the same `UnitOfWorkBehavior` transaction. The scheduled `RecoveryProcessorJob` is **removed** — recovery is LAZY, not scheduled. `RecoveryProcessor.Recover` repays the single debt by the renewed allowance and records the per-window `RecoveryPosition`.
- **Verify:** Seed a 460-over-300 excess → single debt 160; advance Daily (renew 20) → debt 140, Daily usable 0; advance Monthly (renew 300) → debt 0, Monthly usable 140, Daily/Weekly usable again. No per-window debt bucket remains.

### P1-2 — Shared event contracts (M3)
- **Fix:** Introduce explicit versioned DTOs for the events PolicyService consumes/produces (or a shared contract assembly), replacing inline `Payload.TryGetProperty(...)` parsing. Align property names with AuthorizationService producers.
- **Verify:** Contract test asserts producer payload shape == consumer parse shape; a producer rename fails CI.

### P1-3 — Tenant↔consumer ownership gate (M5)
- **Fix:** In `RecordConsumption`/`RecoverDebt`, verify the `ConsumerId` belongs to `TenantId` (via the principal-hierarchy read model or a tenancy check) before operating; reject otherwise.
- **Verify:** A request with a `(tenantId, consumerId)` pair the caller does not own is rejected.

---

## Phase 2 — MEDIUM (launch-adjacent)

- **M1 — Indexes:** add `HasIndex(p => new { p.TenantId, p.Scope })` on `QuotaPolicyConfiguration`/`SubscriptionConfiguration`; add `(TenantId, UserId)`/`(TenantId, RoleId)` indexes on `PrincipalEdgeConfiguration`. Verify `GetByScopeAsync` uses the index (EXPLAIN).
- **M4 — Role scope:** complete principal-hierarchy hydration (M8) so Role-scope quota/subscription resolution is honored; then add Role to `candidateScopes` in `RecordConsumption`.
- **M6 — Cache tenant-scoping:** confirm `CachingBehavior` write keys include tenant (no cross-tenant leakage); add a contract test.
- **M7 — Enable audit:** set `Behaviors:EnableAudit=true` (or confirm an alternative audit sink) so policy/quota/reset/debt events are recorded.
- **M8 — Hierarchy hydration:** emit/consume `UserCreated`/`UserTenantChanged` (or repurpose `role-assigned` to also upsert the user node + ledgers) so `UpsertUserNode` is reachable and the graph is complete.
- **M9 — Idempotency resilience:** confirm `DistributedIdempotencyStore` degrades safely (circuit-break/fallback) when Redis is down; document the behavior.
- **M10 — OPA/outbox reconciliation:** on OPA publish failure after DLQ, provide a replay/restart path (e.g., re-publish from `policy_dead_letter`, or a reconciler that re-syncs OPA from `policies` where `Status=Published`). Add an alert on OPA DLQ depth.

---

## Phase 3 — LOW (cleanup, non-blocking)

- **L1:** Remove dead `IMessagePublisher`/`RabbitMqMessagePublisher` (or document why retained).
- **L2:** Fix build warnings — drop unused `requestContext` injection in `UpsertPrincipalEdgeCommandHandler`; remove duplicate `using`; track the Npgsql internal-API warning (EF1001) for upgrade risk.
- **L3/L4 (superseded):** The `RecoveryProcessorJob` no longer exists; recovery is lazy first-access in `AllowanceEngine`. No watermark or `ListDebtorsAsync` full-scan applies. Remove this item.
- **L5:** Remove redundant `PrincipalEdge` index.

---

## Verification gate (exit criteria for GO)

1. `dotnet build` clean (0 errors).
2. `PolicyService` boots against a fresh DB; `ApplyMigrationsAsync` creates schema; `policy_outbox`/`policy_dead_letter` have **no** RLS policy.
3. End-to-end smoke: create policy → publish → outbox dispatches → OPA receives policy document → `policy.policy-published.v1` cache-invalidation + (if role events) hierarchy consumers fire.
4. Authorization: unprivileged user → `403` on publish/reset; service principal → success; AuthorizationService outage → `503` (fail-closed), not silent allow.
5. Consumption: record over quota → debt incurred (never truncated); recovery reduces debt; Daily/Weekly debt also recoverable (post M2).
6. Tenant isolation: a request scoped to tenant A cannot read/write tenant B data (RLS + global filter + no header-accepted tenant).
7. Secrets: prod boots only with secret-store/env-supplied signing key + connection strings.

**Sign-off required before Phase 0 implementation begins.**
