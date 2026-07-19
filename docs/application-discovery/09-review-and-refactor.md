# Policy Service — Critical Self-Review, Weaknesses & Refactorings

- **Status:** Proposed (discovery, pre-implementation). **This is the mandatory review gate before approval.**
- **Date:** 2026-07-15
- **Depends on:** `00`–`08`.
- **Method:** re-examine the discovery set for internal contradictions, ADR-violations, risks inherited from the Domain audit (F-1..F-5), and architectural weaknesses; propose refactorings **only where they do not regress the approved Domain**.

---

## 1. Internal Consistency Check

| Check | Result |
|-------|--------|
| Every command is `ITransactionalRequest`; every query is not (`02`,`06`) | ✔ consistent |
| Handlers pure; outbox in `SaveChangesAsync` (`03` §3) | ✔ consistent with ADR-015 |
| UC-20/25 multi-aggregate tx is same-store, per-consumer (`02` §3) | ✔ consistent with ADR-018 §9 |
| OPA sync driven by `PolicyPublished` not `SetCompiledRego` | ✔ `SetCompiledRego` raises no event (F-4) but `Publish` fires after it in UC-02 — consistent |
| No runtime call to AuthorizationService (`04` §3) | ✔ hydration-only via events (ADR-014) |
| Cache never wraps the UC-20 hot path (`05` §2) | ✔ protects strong-consistency boundary |
| Audit via outbox, not `AuditBehavior` (`04` §5, `06` §1) | ✔ ADR-017; `AuditBehavior` OFF by default |
| UC-20 is the **only** non-`IAuthorizableRequest` command (system op; FailClosed-safe) | ✔ consistent across `00` §4.4 / `01` §2.1 / `06` §2,§5 |
| OPA sync primitive is `IOpaDataUpdater.SyncPolicyDataAsync` (no stray `IOpaBundleDistributor`) | ✔ consistent across `01` §9 / `03` / `04` §4 |

No contradictions found across `00`–`08`.

---

## 2. Risks Inherited from the Domain Audit

| Finding | Impact on Application | Mitigation |
|---------|----------------------|------------|
| **F-1** specs bypassed by `AllowanceEngine` inline logic | Application must call the engine (not the specs); the specs are reference-only. Risk: engine may diverge from documented spec semantics. | Keep specs as canonical reference; if divergence is found at P2, raise a Domain follow-up (do **not** re-implement in App). |
| **F-2** orphaned VOs (`TenantId`,`RoleId`,`DepartmentId`,`ResourceType`,`ResourceId`) + dead `ResolutionOrder` | Unused types add noise but are harmless to App wiring. | Leave in Domain; optional cleanup ADR later. No App action. |
| **F-3** event payload gaps vs catalog | Some events omit fields a consumer might want (e.g. `OperationAllowed/Denied` carry minimal data). | Verify OPA/Analytics consumers need only what events carry; extend event payloads via a **Domain** change if required (post-approval). |
| **F-4** `Policy.SetCompiledRego` raises no event | OPA sync relies on `PolicyPublished` occurring after `SetCompiledRego` — verified safe in UC-02. | No change required; documented in `04` §4. |
| **F-5** debt-recovery scope ambiguity (was Monthly-only) | Resolved: debt is a SINGLE liability per (Consumer, Action), repaid debt-first by ANY window renewal, triggered **lazily** by `AllowanceEngine` (no `RecoveryProcessorJob` scheduler; ADR-018 §11b). `RecoveryProcessor.Recover` takes the elapsed window + its renewed allowance. | Reconciled in ADR-018 + design-review `02`; no scheduler remains. |

---

## 3. New Architectural Risks (Application-layer)

| ID | Risk | Severity | Mitigation |
|----|------|----------|------------|
| **R-1** | EF cannot directly map the ledgers' `private Dictionary<,>` fields; the approved Domain uses them. | **High** | Spike in P1 (`03` §4.2) using `OwnsMany` + field access. If it fails, a Domain refactor (expose `IEnumerable` + `Add`) is required via follow-up ADR + re-audit. |
| **R-2** | `GetOrCreateAsync` concurrency: two first-time consumers race to create a ledger. | Med | Unique `(tenant_id, consumer_id)` constraint + optimistic retry; idempotent create inside the tx. |
| **R-3** | OPA document-path scheme / removal verb unconfirmed. | Med | TODO in `04` §4; default `policy/<tenantId>/<policyId>` PUT, deletion TBD with OPA team. |
| **R-4** | Inbound AuthorizationService `EventTypeName` strings not yet bound. | Med | TODO in `04` §3; read `AuthorizationService.Domain.Events` before P4. |
| **R-5** | Cache invalidation lag (eventual) could serve a stale effective-subscription/quota for ≤ TTL after a config change. | Low | TTLs are short (5 min); staleness bounded and non-correctness-breaking (re-resolved next window). |
| **R-6** | UC-20 idempotency key design (at-least-once redelivery must not double-count). | Med | **Resolved:** `RecordConsumptionCommand` carries a caller-supplied `IdempotencyKey` (the source feed event id); `IIdempotentRequest` keys on `consumption:{tenantId}:{idempotencyKey}`. `IRetryableRequest` covers transient failures. No in-handler counter mutation occurs until the idempotency check passes (per `06` §2, `08` P2). |
| **R-7** | Per-consumer optimistic concurrency (`Version`) vs. reload of entire `Dictionary<>` of counters on conflict — a conflict on one window reloads all. | Low | Acceptable: contention is per-consumer only; retry is cheap at this scale. |

---

## 4. Architectural Weaknesses (honest assessment)

1. **Two ledgers, one transaction, full reload on conflict (R-7).** Modeling usage + debt as separate aggregates doubles write contention for the same consumer. This is mandated by ADR-018 §9 (debt survives rollover, separate lifecycle), so it is a *correct* trade-off, not a defect — but it is the service's chief scaling ceiling. Mitigation: per-consumer sharding/partitioning at the DB level if throughput demands.
2. **Resolution depends on a hydrated read model (ADR-014).** Until UC-26–29 have run, a brand-new tenant has no hierarchy node and resolution fails closed. Mitigation: provision the user node + ledgers eagerly on first consumption if missing (graceful degrade), rather than hard-fail. **Add to P2/P4 as a resilience task.**
3. **OPA sync is best-effort and unconfirmed (R-3).** A failed OPA push leaves PolicyService durable but OPA stale; consumption still enforces locally, so this is non-fatal, but policy *conditions* (ABAC) would be evaluated against stale Rego until retry succeeds. Acceptable given at-least-once + dedup.
4. **Spec/engine divergence (F-1).** The domain ships specs that the engine ignores. Architecturally this is a smell; the Application correctly ignores them too, but the canonical contract is now "whatever the engine does." Recommend a future Domain ADR to either wire the specs into the engine or delete them.

---

## 5. Proposed Refactorings (post-approval, Domain-owned unless noted)

| Refactoring | Owner | When |
|-------------|-------|------|
| Map ledgers via `OwnsMany` (R-1) — if spike passes, **no** domain change needed. | Application | P1 |
| If R-1 fails: refactor `UsageLedger`/`DebtLedger` to expose `IEnumerable<VO>` + `Add` mutator. | **Domain** (follow-up ADR + re-audit) | only if spike fails |
| Wire `I*Specification` into `AllowanceEngine`/`RecoveryProcessor` (resolve F-1). | **Domain** | optional future |
| Add `PolicyCompiledDomainEvent` on `SetCompiledRego` (resolve F-4 cleanly). | **Domain** | optional future |
| Eager ledger + hierarchy provisioning on first consumption (weakness #2). | Application | P2/P4 resilience task |
| Remove orphaned VOs / dead `ResolutionOrder` (F-2). | **Domain** | cosmetic, optional |

**No Application-layer refactoring is required to proceed.** All weaknesses are either mandated by ADR-018, mitigated by the planned design, or owned by the (already-approved) Domain.

---

## 6. Verdict

The Application discovery set (`00`–`08`) is **internally consistent**, **ADR-conformant** (012/013/014/015/016/017/018), and **grounded in the shipped AuthorizationService/Platform/SharedKernel reference patterns**. The only true blockers are the **P1 EF-mapping spike (R-1)** and two external confirmations (**R-3 OPA path, R-4 inbound event names**). None block *approval* — they block specific later phases and are tracked as TODOs.

**Recommendation: APPROVE this discovery and proceed to P0 → P1 (spike first).** No Application code is written until this document is signed off.

**Next step (human):** approve → I implement P0/P1 per `08`. Or request changes.
