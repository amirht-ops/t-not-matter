# Policy Service — Invariant Report

All invariants are derived from business rules + ADRs. Each invariant states: **what must always hold**, **where enforced**, **why**, and **how violated**.

## A. Quota / Debt accounting invariants

| # | Invariant | Enforcement point | Why | Violation handling |
|---|-----------|-------------------|-----|--------------------|
| 1 | Allowance & limits are never negative. | `Quota` VO factory; `DebtLedger`. | Quota is a grant, not a liability. | `Error.Validation` at construction. |
| 2 | Debt is never negative; it is an outstanding non-negative amount. | `Debt` VO + `DebtLedger.Incur/Recover`. | Debt = liability; "negative debt" is nonsensical (no credit). | Reject operation; `Error.Validation`. |
| 3 | Consumption **always succeeds**; results never truncated to fit quota. | `UsageLedger.Record`. | Business rule: recording factual usage must not be capped. | n/a (no truncation). |
| 4 | Excess over window ⇒ debt (single (action) liability; repaid debt-first by ANY window renewal). | `DebtLedger.IncurDebt`. | Excess is a real liability owed back. | Emit `DebtIncurred` + `QuotaExceeded`. |
| 5 | Debt survives period rollover; recovery deducts debt first: `Available = RenewedAllowance − OutstandingDebt`. | `RecoveryService` + `DebtLedger.Recover`. | Debt cannot silently vanish; future allowance repays it. | n/a. |
| 6 | No access until debt reaches zero. | `ConsumptionService` (pre-check). | Block usage while liability open. | Emit `OperationDenied`. |
| 7 | Any outstanding debt **dominates** all windows (`OutstandingDebt(action) > 0` blocks Daily/Weekly/Monthly). Debt is a single per-action liability, NOT window-scoped (authoritative business rule). | `AllowanceEngine` check on `OutstandingDebt(action)`. | Any debt blocks all usable allowance until repaid. | Emit `OperationDenied` while debt remains. |

## B. Hierarchy / resolution invariants

| # | Invariant | Enforcement | Why | Violation |
|---|-----------|-------------|-----|-----------|
| 8 | Subscription/QuotaPolicy resolution is deterministic: User → Role → Tenant, first-defined wins. | `PrincipalHierarchyReadModel` + resolver. | Reproducibility of policy decisions; no ambiguity. | Deterministic tie-break (earliest effective date). |
| 9 | A scope resolves to exactly one effective policy/quota at each level. | Subscription/QuotaPolicy repo + resolver. | Prevent conflicting effective config. | `Error.Conflict` if ambiguous. |

## C. Lifecycle / reset invariants

| # | Invariant | Enforcement | Why | Violation |
|---|-----------|-------------|-----|-----------|
| 10 | Reset clears usage + debt + counters + derived state **without corrupting history**. | `ResetService` (UsageLedger.Reset + DebtLedger.Reset). | Admin corrective action; audit trail in AuditService remains. | n/a; external audit untouched. |
| 11 | Published Policy is immutable; change requires new version. | `Policy` state guard. | Reproducibility / auditability of published rules. | `Error.Conflict`. |
| 12 | A Subscription cannot reference a non-published / non-existent Policy. | `Subscription.Assign` guard. | Integrity of bindings. | `Error.NotFound`/`Validation`. |

## D. Cross-service / ADR invariants

| # | Invariant | Enforcement | Why | Violation |
|---|-----------|-------------|-----|-----------|
| 13 | `TenantId` always present and equals requesting tenant; never resolved in domain. | Command/aggregate guards (ADR-013). | Tenant is auth-context, not domain logic. | `Error.Validation`. |
| 14 | Domain events carry `TenantId` + `CorrelationId` only; never `RequestContext`. | Event types (ADR-012). | Infrastructure state must not leak into domain. | Compile-time: events have no RequestContext property. |
| 15 | PolicyService never calls Tenant/Authorization/Identity at request time. | Architecture (ADR-011/013/014). | No synchronous cross-service coupling. | Design review / tests. |
| 16 | Quota thresholds are **never** encoded in Rego/OPA. | `RegoModule` generation + ADR-018. | OPA evaluates ABAC only; quota/debt gated in domain. | Code review; rejected by design. |
| 17 | Audit-relevant events published to AuditService via Outbox. | `UnitOfWorkBehavior` + Outbox (ADR-015/017). | Mandatory audit integration. | Pipeline/test failure. |

## E. Event / consistency invariants

| # | Invariant | Enforcement |
|---|-----------|-------------|
| 18 | A consumption command updates UsageLedger + DebtLedger atomically in one transaction. | `UnitOfWorkBehavior` (ADR-015). |
| 19 | Every state-changing command raises a domain event; events are the only out-of-aggregate communication. | `AggregateRoot.RaiseDomainEvent`. |
| 20 | Ledgers are created lazily and never double-created for same natural key. | `IUsageLedgerRepository.GetOrCreateAsync` / `IDebtLedgerRepository.GetOrCreateAsync`. |
