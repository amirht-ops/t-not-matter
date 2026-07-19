# Policy Service — Domain Discovery Report (Part 2: Phases 8–14)

## Phase 8 — Domain Events (catalog; full in `05-event-catalog.md`)

Naming convention (from AuthorizationService/IdentityService): `abstract record XDomainEvent(Guid TenantId, Guid CorrelationId) : IDomainEvent` with `EventTypeName = "policy.<name>.v1"`, `EventId`, `CausationId`, `OccurredAt`, `Version = 1`.

Aggregate → key events:
- **Policy**: `PolicyCreated`, `PolicyPublished`, `PolicyArchived`, `PolicyConditionChanged`.
- **Subscription**: `SubscriptionAssigned`, `SubscriptionActivated`, `SubscriptionRevoked`, `SubscriptionSuperseded`.
- **QuotaPolicy**: `QuotaPolicyDefined`, `QuotaPolicyAmended`, `QuotaPolicyRemoved`.
- **UsageLedger / DebtLedger**: `UsageRecorded`, `UsageReset`, `DebtIncurred`, `DebtRecovered`, `DebtReset`, `QuotaExceeded`, `OperationAllowed`/`OperationDenied` (derived, emitted to OPA layer).

All events include `TenantId` + `CorrelationId`; none include `RequestContext`. Audit-relevant events (`PolicyPublished`, `SubscriptionRevoked`, `UsageReset`, `DebtReset`) are outbox→AuditService per ADR-017.

## Phase 9 — Aggregates cross-boundary consistency → Saga

Cross-aggregate invariants are coordinated by **domain services / saga**, not nested aggregates.

**Consumption saga (per operation):**
1. Load `DebtLedger(consumer)` → if `OutstandingDebt(action) > 0` (single per-action debt, never window-scoped) → **deny** (invariant 7, dominance); emit `OperationDenied`.
2. Load `QuotaPolicy(scope)` (resolved via principal-hierarchy read model) → compute available.
3. Load `UsageLedger(consumer, action)` → increment per-window counter.
4. If total usage across windows > effective limit → `DebtLedger.IncurDebt(totalExcessForAction)` records the SINGLE (action) debt (NOT per-window cascade). Emit `DebtIncurred` + `QuotaExceeded`.
5. Emit `UsageRecorded` + `OperationAllowed`.
6. `UnitOfWorkBehavior` commits UsageLedger+DebtLedger+Outbox in one transaction (ADR-015).

**Recovery (window rollover) — Lazy First-Access (no scheduler):** on the first request after a
window boundary, `AllowanceEngine` detects every elapsed renewal event and calls
`DebtLedger.Recover(renewedAllowance)` (debt-first, per action) → persists per-window
`RecoveryPosition` → emits `DebtRecovered`. Debt survives into the next cycle and is repaid by any
window's renewal. The scheduled `RecoveryProcessorJob` is removed (ADR-018 §11b).

**Reset saga (admin):** `ResetCommand` → `UsageLedger.Reset()` + `DebtLedger.Reset()` → emit `UsageReset`+`DebtReset`, preserving external AuditService history.

Saga orchestration handled at application layer (handler) reading aggregates; no external orchestrator required because all participants share one transaction.

## Phase 10 — Consistency boundaries (detailed)

| Boundary | Participants | Invariant enforced |
|----------|--------------|---------------------|
| Consumption | UsageLedger + DebtLedger + resolved QuotaPolicy (read-only) | invariants 3,4,5,6,7 |
| Recovery | DebtLedger + UsageLedger | invariants 5,6 |
| Subscription resolution | Subscription + principal-hierarchy read model | invariant 8 |
| Config change | QuotaPolicy / Policy / Subscription individually | invariants 1,9 |
| Reset | UsageLedger + DebtLedger | invariant 9 |

Read models (principal hierarchy) are **eventually consistent**, hydrated from AuthorizationService/TenantService/IdentityService events. They are never the source of truth for identity; only for resolution ordering.

## Phase 11 — Quota/Debt settlement rules (explicit algorithms)

- **Window ordering / dominance:** Daily ⊂ Weekly ⊂ Monthly. Debt is a SINGLE liability per (Consumer, Action) — **not** per window. Any outstanding debt dominates all windows (invariant 7): while `OutstandingDebt(action) > 0`, Daily/Weekly/Monthly are treated as blocked until repaid.
- **Recovery & renewal (per renewal event, lazy):** On any window rollover, renewed allowance `R` for an action. `Available = R − OutstandingDebt`. Each operation consumes from `Available`; when `Available` hits 0, consumption goes to debt. Debt is reduced debt-first by ANY window's renewal (Daily/Weekly/Monthly), monotonically; never negative. `RecoveryPosition` (per window) is persisted as the post-repayment usable allowance.
- **Negative Monthly Debt:** if monthly allowance also exceeded, debt grows (outstanding debt increases) → continues to block. There is no "negative quota", only growing non-negative debt.
- **Reset semantics:** only admin-initiated; zeroes `UsageLedger` counters and `DebtLedger` outstanding per consumer. Does **not** change `QuotaPolicy`, `Subscription`, or `Policy`; audit events already emitted to AuditService remain.

## Phase 12 — Proposed file structure (validation vs conventions)

```
src/Services/PolicyService/PolicyService.Domain/
  Aggregates/
    Policies/           Policy.cs, PolicyId.cs, (PolicyVersion.cs child)
    Subscriptions/      Subscription.cs, SubscriptionId.cs, SubscriptionScope.cs
    QuotaPolicies/      QuotaPolicy.cs, QuotaPolicyId.cs, Quota.cs (VO)
    UsageLedgers/       UsageLedger.cs, UsageLedgerId.cs, UsageCounter.cs (VO)
    DebtLedgers/        DebtLedger.cs, DebtLedgerId.cs, Debt.cs (VO), RecoveryPosition.cs (VO)
  ValueObjects/         ActionKey.cs, ConsumerId.cs, TenantId.cs, RoleId.cs,
                        DepartmentId.cs, UserId.cs, ResourceType.cs, ResourceId.cs,
                        PolicyCondition.cs, PolicyExpression.cs, PolicyPriority.cs,
                        RegoModule.cs, QuotaWindow.cs (enum)
  Events/               PolicyDomainEvents.cs, SubscriptionDomainEvents.cs,
                        QuotaPolicyDomainEvents.cs, LedgerDomainEvents.cs
  Errors/               PolicyErrors.cs
  Services/             ConsumptionService.cs, RecoveryService.cs, ResetService.cs
  ReadModels/          PrincipalHierarchyReadModel.cs
  PolicyService.Domain.csproj
```

**Validation against existing conventions:**
- Matches `AuthorizationService.Domain` layout (`Aggregates/`, `ValueObjects/`, `Events/`, `Errors/`).
- Matches `IdentityService.Domain` layout.
- Events pattern matches `AuthorizationDomainEvents`/`IdentityDomainEvents` (abstract record + TenantId/CorrelationId + EventTypeName).
- VOs match `Email`/`SubjectId` (sealed `: ValueObject`, `Create` + `GetEqualityComponents`).
- Aggregate base matches `AggregateRoot` (private ctor + `Create` → `Result<T>` + `RaiseDomainEvent` + `DomainEvents`).
- Errors match `AuthorizationErrors` (static class, `Error.Validation/Conflict/NotFound`).
- No `RequestContext` inside domain (ADR-012) ✓. `TenantId` is a VO, not resolved in domain (ADR-013) ✓.

## Phase 13 — Repository pattern

```
IPolicyRepository            GetByIdAsync(TenantId, PolicyId), AddAsync, UpdateAsync, ListByTenant
ISubscriptionRepository      GetByIdAsync(TenantId, SubscriptionId), GetByScopeAsync(TenantId, SubscriptionScope), AddAsync, UpdateAsync
IQuotaPolicyRepository       GetByIdAsync(TenantId, QuotaPolicyId), GetByScopeAsync(TenantId, SubscriptionScope), AddAsync, UpdateAsync
IUsageLedgerRepository       GetByIdAsync(TenantId, UsageLedgerId), GetOrCreateAsync(TenantId, ConsumerId, ActionKey), UpdateAsync
IDebtLedgerRepository        GetByIdAsync(TenantId, DebtLedgerId), GetOrCreateAsync(TenantId, ConsumerId), UpdateAsync
```

All repo methods tenant-scoped (ADR-013). `GetOrCreateAsync` for ledgers (lazy creation invariant from Phase 6). Matches `IAuthorizationSubscriptionRepository` style (TenantId + id params).

## Phase 14 — Integration surfaces

- **Outbox (ADR-015/017):** `DbContext` maps `DomainEvents`→`OutboxMessage`; `UnitOfWorkBehavior` commits; AuditService consumes audit events.
- **OPA / Rego (ADR-003):** `RegoModule` VO is the compiled artifact. PolicyService generates/compiles/distributes Rego to OPA; **never** encodes quota thresholds in Rego (ADR-018 rejects Rego-encoded quota). OPA evaluates ABAC conditions only; quota/debt gating done in domain, surfaced to OPA as allow/deny verdict.
- **Subscription resolution read model:** hydrated from AuthorizationService (`RoleAssigned`, `DepartmentLinkedToRole`) + TenantService (`TenantCreated`) + IdentityService (`UserCreated`) events. Local only; never synchronous calls.
- **Cross-service events consumed:** role/tenant/user lifecycle events (to maintain read model). **No synchronous dependency** on Authorization/Tenant/Identity at request time.

---
*Next: `03-aggregate-discovery-report.md`, `04-invariant-report.md`, `05-event-catalog.md`, `06-implementation-plan.md`.*
