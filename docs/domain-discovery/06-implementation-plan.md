# Policy Service — Domain Folder Structure, Implementation Plan & Ordered TODO List

> **Gating rule (precedes all implementation):** `07-policy-execution-pipeline.md` (and its
> ADR-018 §20 section) defines the canonical runtime sequence. **No Domain class may be written
> until the pipeline — and its aggregate/service/event mapping — is approved.** Each aggregate's
> method signatures, transaction boundaries, and event emissions are derived from the pipeline.

## 1. Domain Folder Structure (validated)

```
src/Services/PolicyService/
  PolicyService.Domain/
    Aggregates/
      Policies/         Policy.cs, PolicyId.cs           (+ PolicyVersion.cs optional child)
      Subscriptions/    Subscription.cs, SubscriptionId.cs, SubscriptionScope.cs
      QuotaPolicies/    QuotaPolicy.cs, QuotaPolicyId.cs, Quota.cs
      UsageLedgers/     UsageLedger.cs, UsageLedgerId.cs, UsageCounter.cs
      DebtLedgers/      DebtLedger.cs, DebtLedgerId.cs, Debt.cs, RecoveryPosition.cs
    ValueObjects/       ActionKey.cs, ConsumerId.cs, TenantId.cs, RoleId.cs,
                        DepartmentId.cs, UserId.cs, ResourceType.cs, ResourceId.cs,
                        PolicyCondition.cs, PolicyExpression.cs, PolicyPriority.cs,
                        RegoModule.cs, QuotaWindow.cs
    Events/             PolicyDomainEvents.cs, SubscriptionDomainEvents.cs,
                        QuotaPolicyDomainEvents.cs, LedgerDomainEvents.cs
    Errors/             PolicyErrors.cs
    Services/           ConsumptionService.cs, RecoveryService.cs, ResetService.cs
    ReadModels/         PrincipalHierarchyReadModel.cs
    PolicyService.Domain.csproj
  PolicyService.Application/   (commands/handlers, uses repos + UnitOfWorkBehavior)
  PolicyService.Infrastructure/(EF Core, repo impls, Outbox, OPA/Rego distribution)
  PolicyService.Api/           (endpoints; TenantId from accessor)
```

Convention match: identical layout to `AuthorizationService.Domain` / `IdentityService.Domain`; events/VOs/aggregate roots follow their exact patterns; no `RequestContext` in domain.

## 2. Implementation Plan (phased, dependency-ordered)

**Phase A — Foundations (no behavior):** VOs, Errors, AggregateRoot base usage, events, csproj.
**Phase B — Aggregate cores:** Policy, Subscription, QuotaPolicy (config aggregates; low risk).
**Phase C — Operational aggregates:** UsageLedger, DebtLedger (high-churn; lazy creation).
**Phase D — Domain services:** ConsumptionService (saga logic), RecoveryService, ResetService.
**Phase E — Read model:** PrincipalHierarchyReadModel hydrated from external events.
**Phase F — Application/Infra:** repositories, handlers, Outbox wiring, OPA/Rego distribution.
**Phase G — Tests & validation:** invariant tests, saga tests, ADR-018 conformance.

## 3. Ordered TODO List (implementation-ready)

| # | Task | Priority | Depends on | Affected Project | Affected Namespace | Expected Output |
|---|------|----------|-----------|------------------|--------------------|-----------------|
| 1 | Create `PolicyService.Domain.csproj` + folders | High | — | PolicyService.Domain | — | Buildable empty project |
| 2 | Implement identity/key VOs (`TenantId`,`RoleId`,`DepartmentId`,`UserId`,`ConsumerId`,`ActionKey`) | High | 1 | PolicyService.Domain | PolicyService.Domain.ValueObjects | Sealed VOs w/ Create + GetEqualityComponents |
| 3 | Implement config VOs (`PolicyCondition`,`PolicyExpression`,`PolicyPriority`,`RegoModule`,`QuotaWindow`,`ResourceType`,`ResourceId`) | High | 1 | PolicyService.Domain | ValueObjects | Immutable VOs |
| 4 | Implement `PolicyErrors` (Validation/Conflict/NotFound) | High | 1 | PolicyService.Domain | PolicyService.Domain.Errors | Static error catalog |
| 5 | Implement event base + all domain events (4 files) | High | 1 | PolicyService.Domain | PolicyService.Domain.Events | Records w/ EventTypeName |
| 6 | Implement `Policy` aggregate (Draft→Publish→Archive) | High | 2,3,4,5 | PolicyService.Domain | PolicyService.Domain.Aggregates.Policies | Aggregate w/ events |
| 7 | Implement `Subscription` aggregate + `SubscriptionScope` | High | 2,5,6 | PolicyService.Domain | Aggregates.Subscriptions | Binding aggregate |
| 8 | Implement `QuotaPolicy` aggregate + `Quota` VO | High | 2,3,5 | PolicyService.Domain | Aggregates.QuotaPolicies | Config aggregate |
| 9 | Implement `UsageLedger` + `UsageCounter` (lazy, per-action) | High | 2,3,5 | PolicyService.Domain | Aggregates.UsageLedgers | High-churn aggregate |
| 10 | Implement `DebtLedger` + `Debt` + `RecoveryPosition` | High | 2,3,5 | PolicyService.Domain | Aggregates.DebtLedgers | Liability aggregate |
| 11 | Implement `ConsumptionService` (saga: debt block→quota→usage→debt) | High | 6,7,8,9,10 | PolicyService.Domain | PolicyService.Domain.Services | Consumption logic + invariants 3-7 |
| 12 | Implement `RecoveryService` (rollover recovery) | High | 10,11 | PolicyService.Domain | Services | `Available=R−Debt` logic |
| 13 | Implement `ResetService` (admin reset, no history corruption) | High | 9,10,11 | PolicyService.Domain | Services | Reset w/ invariant 10 |
| 14 | Implement `PrincipalHierarchyReadModel` (from ext events) | Medium | — | PolicyService.Domain | PolicyService.Domain.ReadModels | Resolution ordering (inv 8) |
| 15 | Repositories (5 interfaces + impls) | High | 6-10 | PolicyService.Domain / Infrastructure | Repositories | Tenant-scoped repos |
| 16 | Application handlers + `UnitOfWorkBehavior` wiring | High | 11-15 | PolicyService.Application | Handlers | Transactional commands (ADR-015) |
| 17 | OPA/Rego distribution (generate/compile, no quota in Rego) | Medium | 6,16 | PolicyService.Infrastructure | OPA | RegoModule → OPA (ADR-018) |
| 18 | Outbox→AuditService integration | High | 16 | PolicyService.Infrastructure | Outbox | Audit events (ADR-017) |
| 19 | Invariant + saga unit tests | High | 11-13 | PolicyService.Domain.Tests | Tests | Cover invariants 1-20 |
| 20 | Validate against ADR-018 & supersede prior prototype | High | all | docs + codebase | — | Discovery approved → implement |

## 4. Supersession note
The earlier `PolicyService.Domain` prototype (commit `f90c883`) and its `PolicyService.Domain.md` are **superseded** by this discovery + ADR-018. Specifically rejected by the business model: (a) a single `Quota` aggregate combining config+usage+debt; (b) Rego-encoded quota thresholds. Re-implement per the aggregate boundaries above.
