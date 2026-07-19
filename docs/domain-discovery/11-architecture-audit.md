# PolicyService.Domain — Architecture Validation Gate (Pre-Application)

**Date:** 2026-07-15
**Scope:** `src/Services/PolicyService/PolicyService.Domain` (all 5 aggregates, 26 VOs, 20 events, 8 domain services, 5 specs, repositories, factories, errors, constants).
**Authoritative references:** ADR-012, ADR-013, ADR-014, ADR-015, ADR-017, ADR-018 + discovery docs 00–08.
**Method:** Static read of every Domain source file, cross-checked against the ADRs, the discovery docs, the `SharedKernel` primitives (`AggregateRoot`/`Entity`/`ValueObject`/`IDomainEvent`), and the established conventions of `IdentityService.Domain` and `AuthorizationService.Domain`.

---

## 1. Architecture Audit Report

### Phase 1 — Aggregate Audit

| Aggregate | Boundary | Responsibility | Invariant ownership | Events | Verdict |
|-----------|----------|----------------|---------------------|--------|---------|
| `Policy` | Policy def (ABAC) | Lifecycle: Draft→Published→Archived; immutable once published | immutability (inv 11) | 4 | ✓ Correct Vernon aggregate. Private ctor + `Create→Result` + `RaiseDomainEvent`. No public setters. |
| `Subscription` | Policy→scope binding | Lifecycle + precedence metadata | lifecycle, scope required | 4 | ✓ Correct. |
| `QuotaPolicy` | Quota *definition* | Owns `Quota` VO; amend/remove | quota non-negative | 3 | ✓ Correct. `Quota` is a VO, not an aggregate (ADR-018 §7). |
| `UsageLedger` | Per-consumer operational tally | Record/Reset; in-window counters | consumption always succeeds (inv 3), reset≠config | 4 | ✓ Correct, high-churn collection encapsulated in private `Dictionary`. |
| `DebtLedger` | Per-consumer liability | Incur/Recover/Reset | debt never negative (inv 2), dominance (inv 7) | 5 | ✓ Correct. |

**Findings**
- **No god aggregate.** Each aggregate owns a single consistency boundary. Sizes are appropriate.
- **No anemic behavior.** All state transitions are methods that guard invariants and raise events.
- **Cross-aggregate coordination:** `AllowanceEngine.Consume` mutates **both** `UsageLedger` and `DebtLedger` in one invocation. This is **explicitly sanctioned** by ADR-018 §9 / discovery 07 ("stages 5–7 form the per-consumer strong-consistency boundary … persisted in one transaction by the caller"). It is *by design, documented, and accepted* — **not** a spontaneous cross-aggregate write leaked into the domain. The engine is pure and leaves persistence/transaction to the handler (`UnitOfWorkBehavior`, ADR-015).
- **Invariant leakage:** none detected. Invariants 1–3, 5–7, 10–11 are enforced inside the owning aggregate.
- **Duplicated business rules:** SEE Phase 3 / Finding F-1. Three specification classes encode rules that the engine re-implements inline.

### Phase 2 — Value Object Audit

All 26 VOs are `sealed : ValueObject`, immutable (get-only), equality via `GetEqualityComponents`, factory validation via `Result<T>.Create`, and use `Guard.NotEmpty` where appropriate. `RegoModule` computes a SHA-256 hash (pure BCL, no leakage). `Quota` enforces the window-dominance invariant (monthly ≥ weekly ≥ daily) inside its factory.

**Findings**
- **Primitive obsession:** avoided for the most part. One residual smell — `SubscriptionScope` carries a raw `Guid PrincipalId` discriminated by `PrincipalKind` rather than the typed `UserId`/`RoleId`/`TenantId` VOs. This is acceptable given the discriminator, but see F-2 (orphaned typed-id VOs).
- **Duplicated concepts:** none. `PolicyId`/`SubscriptionId`/`QuotaPolicyId`/`UsageLedgerId`/`DebtLedgerId` are distinct per aggregate (correct identity scoping).
- **SharedKernel compliance:** ✓ `ValueObject` base, `Guard`, `Result` all from `SharedKernel`.
- **Orphaned VOs (F-2):** `TenantId`, `RoleId`, `DepartmentId`, `ResourceType`, `ResourceId` are defined but never referenced in the Domain. `TenantId` (VO) is especially redundant given ADR-013 (tenant is a `Guid` from the accessor, held by `AggregateRoot.TenantId`); having a parallel VO invites confusion.

### Phase 3 — Domain Service Audit

| Service | Placement | Assessment |
|---------|-----------|------------|
| `AllowanceEngine` | Domain service | Correct. Pure orchestration of the two ledgers + resolved quota; mutates aggregates, raises events, no I/O, no transaction. Maps to pipeline stages 4–7. |
| `RecoveryProcessor` | Domain service | Correct. Thin wrapper over `DebtLedger.Recover`. |
| `QuotaResolver` | Domain service | Correct. Repo read + delegates to `IQuotaResolutionSpecification`. Async (needs repo). |
| `SubscriptionResolver` | Domain service | Correct. Repo read + in-memory precedence ordering. Async. |
| `PolicyCompiler` | Domain service | Correct, trivial (delegates to `RegoModule.Create`). |
| `PolicyEvaluator` | Domain service | Correct per ADR-018 §15: ABAC final verdict is OPA's; domain returns `Allow` (gating is quota/debt, not ABAC truncation). Documented and intentional. |
| `RegoGenerationService` | Domain service | Correct. Produces Rego from expression+condition; **quota thresholds deliberately excluded** (ADR-018 §15 rejected). ✓ |

**F-1 (MEDIUM) — Specifications defined but bypassed (duplicated rules).** The discovery (Phase E) introduced three rule specifications as the intended single source of truth:
- `IDebtDominanceSpecification.IsDominant(monthlyDebt)` — *never referenced*; `AllowanceEngine` inlines `monthlyDebt.Value > 0`.
- `IAllowanceSufficiencySpecification.IsSufficient(...)` — *never referenced*; engine does not use it.
- `IRecoveryEligibilitySpecification.IsEligible(debt)` — *never referenced*; `RecoveryProcessor` calls `DebtLedger.Recover` directly.

Consequence: the rule lives in two places (spec + inline). This is exactly the "duplicated business rules / logic belongs in a specification" concern raised in Phases 1 & 3. The inline logic is *currently correct and consistent*, so this is a **consistency/maintainability** defect, not a behavioral one — but it must be resolved so the specification is the single source of truth before Application handlers come to rely on the wrong copy.

`IQuotaResolutionSpecification` IS used (by `QuotaResolver`), so the precedent exists.

### Phase 4 — Event Audit

- **EventTypeName format:** `policy.<name>.v1` (kebab + domain prefix + `.v1`). This **matches `AuthorizationService`** (`authorization.<name>.v1`) and the **ADR-017 recommendation** (R9: standardize on kebab-case `<domain>.<entity>.<action>.v1`). `IdentityService`/`TenantService` use legacy PascalCase — PolicyService correctly follows the *modern* platform convention. ✓
- **Versioning:** all `Version = 1`. ✓ (matches platform; version handling is a separate ADR-017 backlog item).
- **Naming / past-tense:** all events are past-tense business facts (`policy.usage-recorded.v1`, `policy.debt-incurred.v1`). ✓
- **Payloads:** carry only domain VOs/primitives — **no `RequestContext`, no infrastructure types, no DTOs, no serialization concerns** (ADR-012 ✓).
- **CausationId:** present on the interface; PolicyService leaves it unset (`get;` only) — consistent with the platform's eventual-consistency posture.

**F-3 (LOW) — Minor event-payload gaps vs the catalog (05).** Several events omit fields the catalog lists:
- `PolicyPublished` — catalog lists `Version, EffectiveFrom`; impl has neither.
- `PolicyCreated` — catalog lists `Priority`; impl has `Name` only.
- `SubscriptionActivated` / `SubscriptionSuperseded` — catalog lists `EffectiveFrom` / `SupersededBy`; impl omits them.
- `UsageReset` / `DebtReset` — catalog lists a `Reason` (`WindowRollover|Admin`); impl omits reason (the `Reset` methods take only `correlationId`).

These are non-breaking (events can evolve), but the catalog is the published contract for AuditService/OPA consumers; align before wiring the Outbox.

**F-4 (LOW) — `Policy.SetCompiledRego` raises no domain event.** Invariant 19 ("every state-changing command raises a domain event") is satisfied by all other mutators; `SetCompiledRego` is a state change without an event. Add `PolicyCompiledDomainEvent` or explicitly exempt (document why).

### Phase 5 — Pipeline Audit

Walked against discovery 07 (canonical order) and ADR-018 §20:

| Stage | Aggregate / Service | Tx boundary | Emitted events | Match? |
|-------|---------------------|-------------|----------------|--------|
| 1 Subscription Resolution | `SubscriptionResolver` (+repo) | read | — | ✓ |
| 2 Policy Resolution | `IPolicyRepository` + `PolicyEvaluator` | read | — | ✓ (ABAC verdict = OPA; domain allows) |
| 3 Quota Resolution | `QuotaResolver` + `IQuotaResolutionSpecification` | read | — | ✓ |
| 4 Debt Resolution | `DebtLedger` (loaded) | read | — | ✓ |
| 5 Consumption Decision | `AllowanceEngine` | — | `OperationDenied` (if blocked) | ✓ |
| 6 Usage Recording | `UsageLedger.Record` | — | `UsageRecorded` | ✓ |
| 7 Debt Update | `DebtLedger.IncurDebt` | — | `DebtIncurred` + `QuotaExceeded` | ✓ |
| 8 Domain Events | `AggregateRoot.RaiseDomainEvent` | in-memory | all above + `OperationAllowed` | ✓ |
| 9 Outbox | *(Application/Infra — future)* | `UnitOfWorkBehavior` (ADR-015) | `OutboxMessage` | n/a (not yet built) |
| 10 Audit | *(Infra — future)* | downstream | — | n/a |
| 11 OPA Sync | *(Infra — future, Rego only)* | downstream | — | n/a (no quota in Rego ✓) |

**Stages 5–7** are performed inside `AllowanceEngine` over the two ledgers and must be persisted atomically by the caller (ADR-018 §9 / ADR-015). The Domain does not open a transaction — correct. ✓

**F-5 (MEDIUM) — Recovery scope / debt lifecycle ambiguity.** `RecoveryProcessor.Recover` calls `DebtLedger.Recover(action, QuotaWindow.Monthly, …)` only. Daily and Weekly debt is incurred per window (engine records all three windows) but is **never recovered** by the processor (the generic `DebtLedger.Recover` exists but is only invoked for Monthly). Two interpretations:
- (a) Monthly is the sole recovery unit and daily/weekly debt is informational (monthly dominance binds) → current code is fine.
- (b) Each window's debt must be recovered at its own rollover → daily/weekly debt accumulates indefinitely (gap).

The contract is ambiguous (discovery 04 inv 4 / 07 stage 7 used "cascade Daily→Weekly→Monthly" language that the implementation does **not** realize). **Reconciled:** the authoritative business rule states debt is a SINGLE liability per (action), repaid by ANY renewal event; `RecoveryProcessor` is invoked lazily by `AllowanceEngine` (no scheduler). The per-window cascade language has been removed from ADR-018, the invariant report, and the pipeline doc. The single-debt model is internally consistent and matches the latest stated business rules.

### Phase 6 — DDD Audit (Evans / Vernon / eShop / Bogard)

- **Aggregate references:** aggregates reference each other only by **identity VO** (`PolicyId` inside `Subscription`, `SubscriptionScope`/`Quota` VOs) — never by object reference. ✓ (Vernon identity-based referencing)
- **Consistency boundaries:** one transaction per command; the only multi-aggregate command is the documented consumption boundary. ✓
- **Invariants:** enforced in the aggregate that owns them. ✓
- **Ubiquitous language:** `Policy`, `Subscription`, `QuotaPolicy`, `UsageLedger`, `DebtLedger`, `ConsumptionDecision`, `RecoveryPosition` mirror the discovery vocabulary. ✓
- **Transactional boundaries:** Domain is transaction-agnostic; persistence delegated to handler/`UnitOfWorkBehavior` (ADR-015). ✓ (Bogard: domain model shouldn't know about transactions)
- **Repository ownership:** one repo per aggregate, tenant-scoped `GetByIdAsync(tenantId, id, …)` signature, matching `IdentityService`/`AuthorizationService` convention. ✓

### Phase 7 — SharedKernel & Platform Audit

- **Base classes:** every aggregate derives from `SharedKernel.Domain.Primitives.AggregateRoot` (ctor `base(id, tenantId)`, `RaiseDomainEvent`, `MarkUpdated`, `SoftDelete`, `IsDeleted`). `QuotaPolicy` uses `SoftDelete()` for `Remove` — consistent with `User`/`Session`/`Department`. ✓
- **GlobalUsings:** `PolicyService.Domain` has a minimal `GlobalUsings.cs` (`global using …Errors;`); `IdentityService.Domain` and `AuthorizationService.Domain` have **no** `GlobalUsings.cs` and use explicit `using`. Parity is fine (no divergence forced).
- **Errors:** use `SharedKernel.Errors.Error` with `.Validation/.NotFound/.Conflict/.Failure` factories, namespaced `Policy.*` / `QuotaPolicy.*` etc. — matches sibling services. ✓
- **Events:** `PolicyDomainEvent : IDomainEvent` mirrors `IdentityDomainEvents` / `AuthorizationDomainEvents` shape (EventId/OccurredAt/Version set in ctor, abstract `EventTypeName`). ✓
- **No custom patterns** beyond what the discovery/ADRs prescribe. The orphaned specs (F-1) and orphaned VOs (F-2) are the only "extra" surface.
- **Event naming** is kebab + `.v1`, aligning with `AuthorizationService` and ADR-017 R9 (the *recommended* direction), not the legacy PascalCase of `Identity`/`Tenant`. ✓

### Phase 8 — Future Readiness Audit

| Readiness for… | Status | Notes |
|----------------|--------|-------|
| Application (handlers) | ✓ Ready | Engine/resolvers are pure and take already-loaded aggregates; handlers orchestrate. Wire specs (F-1). |
| Infrastructure (EF) | ✓ Ready | Aggregates are POCO-style with private ctors; collections are private `Dictionary` (EF will need mapping config / shadow keys). `GetOrCreateAsync` returning `T?` is slightly misleading — Application should guarantee a non-null instance. |
| Outbox (ADR-017) | ✓ Ready | Events implement `IDomainEvent`; `EventTypeName` is routable. Align payloads (F-3) first. |
| Audit (ADR-017) | ⚠ Minor | Catalog audit flags exist; F-3 payload gaps should be closed so AuditService receives the documented fields. |
| OPA sync (ADR-003/018 §15) | ✓ Ready | `RegoModule` (no quota) is the distribution unit; `PolicyCompiler`/`RegoGenerationService` produce it. |

**Future risks (non-blocking):**
- R-1: Orphaned `RecoveryEligibility`/`AllowanceSufficiency`/`DebtDominance` specs may drift from inline engine logic (F-1).
- R-2: Debt recovery model ambiguity (F-5) — if per-window recovery is required, engine + processor need change.
- R-3: `PrincipalHierarchyReadModel` referenced in discovery 07 stage 1/3 is **not** present; resolution is done via repository queries + in-memory ordering. Functionally correct; a read model can be introduced in Infrastructure for performance/consistency if needed.
- R-4: Orphaned VOs (F-2) add noise and a `TenantId`-VO/ADR-013 tension.
- R-5: `PolicyServiceConstants.ResolutionOrder` is dead code (precedence is computed via enum ordering in the resolvers).

---

## 2. Risk Assessment

| # | Risk | Severity | Impact if unaddressed |
|---|------|----------|------------------------|
| R-1 | Specs bypassed → rule duplication (F-1) | Medium | Engine and specs can diverge; wrong rule consumed by Application. |
| R-2 | Debt recovery scope ambiguity (F-5) | Medium | Daily/Weekly debt may accumulate forever if per-window recovery expected. |
| R-3 | Missing `PrincipalHierarchyReadModel` | Low | Slower/less-consistent resolution at scale; not a correctness issue. |
| R-4 | Orphaned VOs / `TenantId` VO vs ADR-013 | Low | Noise; minor confusion. |
| R-5 | Event payload gaps vs catalog (F-3) | Low | AuditService/OPA receive fewer fields than documented. |
| R-6 | `SetCompiledRego` no event (F-4) | Low | Invariant-19 exception; loses an audit/OPA trigger. |

**No Critical or High risks.** No infrastructure leakage, no `RequestContext`, no cross-aggregate *unsanctioned* writes, no god aggregate, no anemic behavior.

---

## 3. Recommended Refactorings (non-blocking)

1. **(F-1)** Wire the three specifications into the engine/processor as the single source of truth, OR delete them if the inline logic is intentionally the canonical form. Recommended: have `AllowanceEngine` call `IDebtDominanceSpecification.IsDominant` and `IAllowanceSufficiencySpecification.IsSufficient`, and `RecoveryProcessor` call `IRecoveryEligibilitySpecification.IsEligible`, so the rule lives in exactly one place.
2. **(F-5)** Reconciled: the debt model is the single (action) liability, repaid debt-first by any window renewal, triggered lazily (no `RecoveryProcessorJob`). Discovery 04 inv 4 / 07 stage 7 have been updated to drop the "cascade" wording; the decision is documented in ADR-018. No per-window recovery remains.
3. **(F-2)** Remove orphaned VOs (`TenantId`, `RoleId`, `DepartmentId`, `ResourceType`, `ResourceId`) or use them (e.g., have `SubscriptionScope` carry the typed id). Remove `PolicyServiceConstants.ResolutionOrder` if unused.
4. **(F-3)** Align event payloads with catalog 05 (add `EffectiveFrom`/`SupersededBy`/`Priority`/`Reason` where listed) before the Outbox is wired.
5. **(F-4)** Add `PolicyCompiledDomainEvent` (or document the exemption) so `SetCompiledRego` satisfies invariant 19.
6. **(R-3)** Optionally introduce `PrincipalHierarchyReadModel` in Infrastructure if resolution performance/consistency warrants it.

---

## 4. Build Validation

```
dotnet build src/Services/PolicyService/PolicyService.Domain/PolicyService.Domain.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:08.13
```

- No references to EF Core, MediatR, MassTransit, ASP.NET, System.Text.Json, Redis, or Logging namespaces (pure domain).
- `RequestContext` appears only in a comment (ADR-012 compliant).
- All aggregates: private ctor + `Create→Result<T>` + `RaiseDomainEvent`; no public setters.

---

## 5. Readiness Report

The implemented `PolicyService.Domain` faithfully realizes the approved business model and ADR-018:

- ✅ Five properly-bounded Vernon aggregates; Quota is a VO; Reset is a command that clears usage+debt without touching `QuotaPolicy`.
- ✅ 26 immutable, validated Value Objects; 20 domain events carrying `TenantId`+`CorrelationId` only, with kebab `.v1` EventTypeNames consistent with `AuthorizationService` and ADR-017.
- ✅ Pipeline matches discovery 07 / ADR-018 §20; `AllowanceEngine` (stages 4–7) is pure and transaction-agnostic, leaving atomic persistence to the caller (ADR-015).
- ✅ ADR-012 (no RequestContext), ADR-013 (TenantId from accessor only), ADR-015 (transaction ownership), ADR-017 (Outbox-ready events), ADR-018 (separate aggregates, no Rego-encoded quota) all satisfied.
- ✅ Build is clean (0/0); no infrastructure leakage; follows `IdentityService`/`AuthorizationService`/`SharedKernel` conventions.
- ⚠ Only non-blocking items remain: spec/inline rule duplication (F-1), debt-recovery scope ambiguity to reconcile (F-5), minor event-payload gaps (F-3), one missing event (F-4), and a few orphaned VOs (F-2). None require a Domain redesign.

---

## Final Verdict

# READY FOR APPLICATION

The Domain model is considered **stable**. Application implementation (handlers, EF mappings, Outbox→AuditService, OPA sync) may begin **without further Domain redesign**.

The following should be closed (ideally before/early in the Application phase, none of which forces Domain changes):
- Reconcile the debt-recovery scope (F-5) and update ADR-018/discovery wording accordingly.
- Wire or remove the three orphaned specifications to establish a single source of truth for the dominance/sufficiency/eligibility rules (F-1).
- Align the few event payloads with catalog 05 (F-3) and add the missing `PolicyCompiledDomainEvent` (F-4) before the Outbox is wired.
- Remove orphaned VOs / dead `ResolutionOrder` constant (F-2) for cleanliness.
