# Policy Service — Policy Execution Pipeline Discovery

**Phase:** Pre-implementation discovery (must precede any Domain class).
**Purpose:** Define the end-to-end execution flow a consumption request follows through the
PolicyService domain, mapping each stage to the discovered aggregates, domain services, events,
and cross-service sinks. This is the canonical **runtime sequence** that every handler/aggregate
must obey; it is the behavioral counterpart to the structural discovery (aggregates, invariants).

## Pipeline (canonical order)

```
Subscription Resolution
        ↓
Policy Resolution
        ↓
Quota Resolution
        ↓
Debt Resolution
        ↓
Consumption Decision
        ↓
Usage Recording
        ↓
Debt Update
        ↓
Domain Events
        ↓
Outbox
        ↓
Audit
        ↓
OPA Sync
```

## Stage-by-stage

| # | Stage | Input | Aggregate / Service | Action | Output |
|---|-------|-------|---------------------|--------|--------|
| 1 | **Subscription Resolution** | ConsumerId, resource/action | `PrincipalHierarchyReadModel` + `ISubscriptionRepository` | Resolve which `Subscription`(s) apply to the consumer's scope (User→Role→Tenant, first-defined wins; ADR-018 §8, §14). | Effective `Subscription` set (+ PolicyId refs) |
| 2 | **Policy Resolution** | Resolved Subscription(s) | `IPolicyRepository` | Load the `Policy`(ies) referenced by subscriptions; evaluate ABAC `PolicyCondition`/`PolicyExpression`. OPA consults `RegoModule` for pure ABAC conditions (ADR-003/018 §15). | Allowed/denied by policy; effective Policy set |
| 3 | **Quota Resolution** | Consumer scope | `QuotaResolutionSpecification` / `IQuotaPolicyRepository` | Walk User→Role→Tenant, return effective `QuotaPolicy` (first defined wins). Read-only reference; no lock. | Effective `QuotaPolicy` (Daily/Weekly/Monthly) |
| 4 | **Debt Resolution** | ConsumerId | `IDebtLedgerRepository` → `DebtLedger` | Load the SINGLE `OutstandingDebt(action)` + per-window `RecoveryPosition`. Apply **debt dominance** (any outstanding debt blocks all windows; inv 7). | Debt state; block flag if `OutstandingDebt(action) > 0` |
| 5 | **Consumption Decision** | Allowance − debt, requested units | `AllowanceEngine` | Compute `Available = RenewedAllowance − OutstandingDebt`. Per business rule, consumption **always succeeds** (inv 3); decision is *allow + meter*, never *reject*. If `OutstandingDebt(action) > 0` → all windows denied (inv 7). | Decision: Allow + meter; excess→debt |
| 6 | **Usage Recording** | ConsumerId, ActionKey, units | `IUsageLedgerRepository` → `UsageLedger` | Increment per-action `UsageCounter` for the window. Never truncated (inv 3). | Updated `UsageLedger`; `UsageRecorded` raised |
| 7 | **Debt Update** | Excess over window | `IDebtLedgerRepository` → `DebtLedger` | If total usage across windows > effective limit, `DebtLedger.IncurDebt(totalExcessForAction)` records the SINGLE (action) debt (NOT per-window cascade). | Updated `DebtLedger`; `DebtIncurred` + `QuotaExceeded` raised |
| 8 | **Domain Events** | All mutations | `AggregateRoot.RaiseDomainEvent` | Each aggregate emits its events (§events). Events carry `TenantId`+`CorrelationId` only (ADR-012). | In-memory `DomainEvents` collection |
| 9 | **Outbox** | DomainEvents | `UnitOfWorkBehavior` + `DbContext` (ADR-015) | On `SaveChangesAsync`, map `DomainEvents`→`OutboxMessage` in same transaction. | `OutboxMessage` rows (durable) |
| 10 | **Audit** | Outbox messages | Outbox dispatcher → AuditService (ADR-017) | Audit-relevant events (`PolicyPublished`, `SubscriptionRevoked`, `UsageReset`, `DebtReset`, `DebtIncurred`, `QuotaExceeded`) delivered to AuditService. | Immutable audit records |
| 11 | **OPA Sync** | Policy/Quota state | PolicyService.Infrastructure (ADR-003/018 §15) | Publish computed effective-allowance facts / updated `RegoModule` to OPA. **Quota/debt thresholds are NOT encoded in Rego** (ADR-018 §15 rejected). OPA is downstream only. | OPA bundle synced |

## Consistency & transaction boundaries

- **Stages 5–7 (decision + usage + debt)** form the **per-consumer strong-consistency boundary**:
  `AllowanceEngine` loads `UsageLedger` + `DebtLedger` (+ reads `QuotaPolicy` by id) and persists
  them in **one transaction** via `UnitOfWorkBehavior` (ADR-018 §9). This guarantees
  "operation succeeds + debt recorded" and "monthly dominance" atomically per consumer.
- **Stages 1–4 (resolution)** are reads against the local `PrincipalHierarchyReadModel` and
  repositories; eventually consistent, never call AuthorizationService/TenantService/IdentityService
  at request time (ADR-011/013/014).
- **Stages 8–11** are atomic with the domain mutation (Outbox in same tx); Audit and OPA Sync are
  downstream consumers and never block the consumption path.

## Mapping to discovered artifacts

- Aggregates: `Subscription`, `Policy`, `QuotaPolicy`, `UsageLedger`, `DebtLedger`
  (see `03-aggregate-discovery-report.md`).
- Domain services: `QuotaResolver`, `AllowanceEngine`, `RecoveryProcessor`,
  `AllowanceAdministrationService` (ADR-018 §11).
- Events: `05-event-catalog.md`. Invariants: `04-invariant-report.md`.
- This pipeline is the runtime realization of the Consumption saga in
  `01-domain-discovery-report-pt2.md` §9.

## Validation against ADRs

- ADR-012: no `RequestContext` in events. ✓ (stage 8)
- ADR-013: `TenantId` from accessor only; never resolved in domain. ✓
- ADR-014: department from Role; resolved via read model, not runtime call. ✓
- ADR-015: single transaction via `UnitOfWorkBehavior`. ✓ (stage 9)
- ADR-017: Outbox→AuditService. ✓ (stage 10)
- ADR-018: separate aggregates; debt dominance; reset≠config; no Rego-encoded quota. ✓
- ADR-003: OPA downstream; PolicyService owns Rego gen. ✓ (stage 11)

## Conclusion
The pipeline is the authoritative execution contract. No Domain class may be written until this
flow (and its aggregate/service/event mapping) is approved, because it dictates the method
signatures, transaction boundaries, and event emissions each aggregate must expose.
