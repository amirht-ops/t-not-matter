# PolicyService.Domain — Dependency Map (Phase 0)

**Purpose:** Enumerate every type in the approved `PolicyService.Domain` design, show its
dependencies, and prove there are **no circular dependencies, no Vernon violations, no
cross-bounded-context dependencies, and no infrastructure leakage** before any code is written.

**Source of truth:** ADR-012/013/014/015/017/018, `docs/domain-discovery/00`–`07`.
**Convention baseline:** `AuthorizationService.Domain`, `IdentityService.Domain`, `SharedKernel`, `Platform`.

---

## 1. Project dependency (only SharedKernel)

```
PolicyService.Domain
      └──> SharedKernel   (AggregateRoot, Entity, ValueObject, IDomainEvent,
                            Result<T>, Error, Guard, Unit)
```

- `PolicyService.Domain` references **no other project** (no Platform, no AuthorizationService,
  no IdentityService, no TenantService). External principals are represented by **Guid-wrapping
  Value Objects**, never by foreign types. ✅ no cross-BC dependency.
- No `Microsoft.EntityFrameworkCore`, no `MediatR`, no `Microsoft.AspNetCore.*`, no caching,
  logging, serialization, or configuration packages. ✅ no infrastructure leakage.

---

## 2. Aggregate dependency graph

```
Policy ─────────────► PolicyId, PolicyStatus, PolicyCondition, PolicyExpression,
│                     PolicyPriority, RegoModule, Policy*DomainEvent, PolicyErrors
│
Subscription ───────► SubscriptionId, SubscriptionScope, SubscriptionStatus,
│                     PolicyId (ref), Subscription*DomainEvent, PolicyErrors
│
QuotaPolicy ────────► QuotaPolicyId, Quota, QuotaLimit, QuotaWindow, SubscriptionScope,
│                     QuotaPolicy*DomainEvent, PolicyErrors
│
UsageLedger ────────► UsageLedgerId, ConsumerId, ActionKey, UsageCounter, QuotaWindow,
│                     Usage*DomainEvent, PolicyErrors
│
DebtLedger ─────────► DebtLedgerId, ConsumerId, ActionKey, DebtAmount, RecoveryPosition,
                      Debt*DomainEvent, PolicyErrors
```

**Vernon check:**
- Each aggregate references others **only by id / Value Object** (Subscription → `PolicyId`,
  QuotaPolicy → `SubscriptionScope`, ledgers → `ConsumerId`). No aggregate embeds another
  aggregate. ✅
- Consistency boundaries: `UsageLedger` + `DebtLedger` are coordinated by `IAllowanceEngine`
  (domain service), not by nesting. ✅
- No aggregate calls a repository or another aggregate's methods. ✅

---

## 3. Value Object dependency graph

```
Identity / scope VOs        → SharedKernel (ValueObject, Result, Guard)
  TenantId, RoleId, DepartmentId, UserId, ConsumerId
  PolicyId, SubscriptionId, QuotaPolicyId, UsageLedgerId, DebtLedgerId

Domain VOs                  → SharedKernel (ValueObject, Result, Guard) + PolicyErrors
  ActionKey, ResourceType, ResourceId
  QuotaWindow (enum), QuotaLimit, Quota (holds 3× QuotaLimit)
  UsageCounter (QuotaWindow + period + count)
  DebtAmount, RecoveryPosition
  SubscriptionScope (principal kind + id VO)
  PolicyCondition, PolicyExpression, PolicyPriority, RegoModule
  ConsumedUnits, ConsumptionDecision
```

Rules:
- VOs depend **only** on `SharedKernel` + `PolicyErrors`. ✅
- VOs never depend on Aggregates, Events, Repositories, or Services. ✅ (acyclic)
- No VO holds an entity reference. ✅

---

## 4. Domain Service dependency graph

```
IRegoGenerationService  → RegoModule, PolicyExpression, PolicyCondition, Result
IPolicyCompiler         → RegoModule, PolicyExpression, Result
IPolicyEvaluator        → Policy, ConsumptionDecision, Result
IQuotaResolver          → QuotaPolicy, SubscriptionScope, QuotaResolutionSpecification, Result
ISubscriptionResolver   → Subscription, SubscriptionScope, Result
IAllowanceEngine        → UsageLedger, DebtLedger, QuotaPolicy, ConsumptionDecision,
                          DebtDominanceSpecification, Result
IRecoveryProcessor      → DebtLedger, QuotaPolicy, RecoveryEligibilitySpecification, Result
IAllowanceAdministrationService → UsageLedger, DebtLedger, Result (Reset)
```

Rules:
- Services depend on **interfaces** (Specifications) and **aggregates/VOs by reference**, never
  on concrete repository implementations. ✅
- Aggregates do **not** depend on services → acyclic. ✅
- Services live in `Services/`; they are contracts only until Phase G. ✅

---

## 5. Repository dependency graph

```
IPolicyRepository        → Policy, PolicyId, TenantId
ISubscriptionRepository   → Subscription, SubscriptionId, SubscriptionScope, TenantId
IQuotaPolicyRepository    → QuotaPolicy, QuotaPolicyId, SubscriptionScope, TenantId
IUsageLedgerRepository    → UsageLedger, UsageLedgerId, ConsumerId, ActionKey, TenantId
IDebtLedgerRepository     → DebtLedger, DebtLedgerId, ConsumerId, TenantId
```

Rules:
- Repositories depend on aggregates + VOs only. ✅
- All methods are **tenant-scoped** (`GetByIdAsync(Guid tenantId, ...)`) per ADR-013. ✅
- Repositories are **interfaces** in the Domain; implementations live in Infrastructure (never
  referenced by the Domain). ✅ no infra leakage.

---

## 6. Event dependency graph

```
PolicyDomainEvent (abstract base : IDomainEvent)
   ├─ PolicyCreatedDomainEvent        → PolicyId, TenantId, CorrelationId
   ├─ PolicyPublishedDomainEvent      → PolicyId, TenantId, CorrelationId
   ├─ PolicyArchivedDomainEvent       → PolicyId, TenantId, CorrelationId
   ├─ PolicyConditionChangedDomainEvent → PolicyId, TenantId, CorrelationId
   ├─ SubscriptionAssignedDomainEvent → SubscriptionId, PolicyId, SubscriptionScope, ...
   ├─ SubscriptionActivatedDomainEvent → SubscriptionId, TenantId, CorrelationId
   ├─ SubscriptionRevokedDomainEvent  → SubscriptionId, TenantId, CorrelationId
   ├─ SubscriptionSupersededDomainEvent → SubscriptionId, TenantId, CorrelationId
   ├─ QuotaPolicyDefinedDomainEvent   → QuotaPolicyId, SubscriptionScope, Quota, ...
   ├─ QuotaPolicyAmendedDomainEvent   → QuotaPolicyId, Quota, TenantId, CorrelationId
   ├─ QuotaPolicyRemovedDomainEvent   → QuotaPolicyId, TenantId, CorrelationId
   ├─ UsageRecordedDomainEvent        → UsageLedgerId, ConsumerId, ActionKey, count, ...
   ├─ UsageResetDomainEvent           → UsageLedgerId, ConsumerId, TenantId, CorrelationId
   ├─ DebtIncurredDomainEvent         → DebtLedgerId, ConsumerId, ActionKey, amount, ...
   ├─ DebtRecoveredDomainEvent        → DebtLedgerId, ConsumerId, ActionKey, amount, ...
   ├─ DebtResetDomainEvent            → DebtLedgerId, ConsumerId, TenantId, CorrelationId
   ├─ QuotaExceededDomainEvent        → DebtLedgerId, ConsumerId, ActionKey, excess, ...
   ├─ OperationAllowedDomainEvent     → ConsumerId, ActionKey, TenantId, CorrelationId
   └─ OperationDeniedDomainEvent      → ConsumerId, ActionKey, reason, TenantId, CorrelationId
```

Rules:
- All events derive from `PolicyDomainEvent(Guid TenantId, Guid CorrelationId) : IDomainEvent`.
- Event payloads use **only VOs + Guids**. No `RequestContext`, no `DateTime` (uses
  `OccurredAt` from base), no infrastructure types. ✅
- Events depend on VOs, not on aggregates. ✅ acyclic.
- `EventTypeName` constants follow `"policy.<name>.v1"`. ✅

---

## 7. Validation

| Check | Result | Evidence |
|-------|--------|----------|
| No circular dependencies | ✅ PASS | VOs → SharedKernel only; Aggregates → VOs; Events → VOs; Services/Repos/Specs → Aggregates/VOs. No back-edges to VOs from Aggregates' dependencies. |
| No aggregate references violating Vernon | ✅ PASS | Aggregates reference each other by id/VO only; coordination via domain service. |
| No cross-bounded-context dependencies | ✅ PASS | Only `SharedKernel` referenced; all external principals are Guid-wrapping VOs. |
| No infrastructure leakage | ✅ PASS | No EF/MediatR/ASP.NET/caching/logging/serialization/config in Domain. Repos are interfaces. |
| Pipeline fit | ✅ PASS | Every stage of ADR-018 §20 maps to a service/aggregate/event above. |

**Conclusion:** the dependency graph is internally consistent. Implementation may proceed.
