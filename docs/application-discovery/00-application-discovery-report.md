# Policy Service — Application Discovery Report

- **Status:** Proposed (discovery, pre-implementation). **No Application class may be written until this discovery is approved.**
- **Date:** 2026-07-15
- **Authoritative inputs:** ADR-012, ADR-013, ADR-014, ADR-015, ADR-017, ADR-018 (+ ADR-003, ADR-011, ADR-016 for OPA/authorization/cache context).
- **Domain baseline:** the completed & approved `PolicyService.Domain` (5 aggregates, 8 domain services, 5 repositories, 4 specifications, 19 domain events).
- **Convention sources:** `SharedKernel.Application` / `SharedKernel.Authorization` markers, `Platform.Behaviors`, `Platform.Abstractions.Tenant.IRequestContextAccessor`, `AuthorizationService.Application` & `TenantService.Application` vertical-slice layout.

> **Gating rule (precedes all implementation):** this report + `01-application-execution-pipeline.md` define the canonical Application-layer contract. Commands, Queries, Handlers, Validators, Repository implementations, EF mappings, and Infrastructure code are **forbidden** until this discovery set is approved. This is an architecture-first exercise, exactly as performed for the Domain.

---

## 1. Purpose & Method

The Domain layer answers *"what are the rules"*. The Application layer answers *"which use cases orchestrate those rules, in what order, under what transaction, and with what integration side-effects"*. This report derives the complete Application surface **only** from (a) the six governing ADRs and (b) the shape the Domain already exposes (aggregate method signatures, domain-service contracts, repository interfaces, specifications, events). No new domain behavior is invented here.

Discovery proceeds: use-case catalog → CQRS classification → command/query ownership → aggregate usage per use case. The runtime sequencing, transactions, integration, cache, pipeline-behavior validation, read models, and the phased plan are in the sibling documents (`01`–`09`), with a mandatory self-review in `10`.

---

## 2. Domain Baseline (the surface the Application sits on)

| Element | Members (as implemented) |
|---------|--------------------------|
| **Aggregate `Policy`** | `Create`, `Publish`, `Archive`, `ChangeCondition`, `SetCompiledRego` |
| **Aggregate `Subscription`** | `Create`, `Activate`, `Revoke`, `Supersede` |
| **Aggregate `QuotaPolicy`** | `Create`, `Amend`, `Remove` |
| **Aggregate `UsageLedger`** | `Create`, `Record`, `GetCount`, `Reset`, `MarkAllowed`, `MarkDenied` |
| **Aggregate `DebtLedger`** | `Create`, `IncurDebt`, `Recover`, `OutstandingDebt`, `MonthlyDebt`, `Reset` |
| **Domain services** | `IRegoGenerationService`, `IPolicyCompiler`, `IPolicyEvaluator`, `ISubscriptionResolver`, `IQuotaResolver`, `IAllowanceEngine`, `IRecoveryProcessor`, `IAllowanceAdministrationService` |
| **Repositories** | `IPolicyRepository`, `ISubscriptionRepository`, `IQuotaPolicyRepository`, `IUsageLedgerRepository`, `IDebtLedgerRepository` |
| **Specifications** | `IQuotaResolutionSpecification`, `IDebtDominanceSpecification`, `IAllowanceSufficiencySpecification`, `IRecoveryEligibilitySpecification` |
| **Events (19)** | Policy: Created/Published/Archived/ConditionChanged · Subscription: Assigned/Activated/Revoked/Superseded · QuotaPolicy: Defined/Amended/Removed · Ledgers: UsageRecorded/UsageReset/DebtIncurred/DebtRecovered/DebtReset/QuotaExceeded/OperationAllowed/OperationDenied |

Every mutating aggregate method returns `Result<T>`/`Result<Unit>` and takes a `Guid correlationId`. Events carry `TenantId` + `CorrelationId` only (ADR-012). This is the exact vocabulary from which the use cases below are derived.

---

## 3. ADR Constraints That Shape the Application Layer

| ADR | Constraint the Application layer must obey |
|-----|--------------------------------------------|
| **ADR-012** | Handlers read `TenantId`/`CorrelationId`/`UserId` **only** from `Platform.Abstractions.Tenant.IRequestContextAccessor.Context`. Commands/Queries never carry a `RequestContext`; events never carry it. Execution context is never cached. |
| **ADR-013** | PolicyService **consumes** the resolved `TenantId` from the accessor; it never resolves tenants. No `slug→tenant` lookup in this service. |
| **ADR-014** | Department/Role membership is owned by AuthorizationService. PolicyService resolves the User→Role→Tenant hierarchy from a **local read model**, never a runtime call to AuthorizationService. |
| **ADR-015** | `UnitOfWorkBehavior` is the **sole** transaction owner. Handlers are **pure**: they mutate aggregates via repositories and never call `BeginTransaction`/`SaveChanges`/`Commit`/`Rollback`/`ClearDomainEvents`. Every mutating command is marked `ITransactionalRequest`. Outbox rows are materialized inside `DbContext.SaveChangesAsync`. |
| **ADR-017** | Domain events → Outbox (same transaction) → dispatcher → RabbitMQ → AuditService & downstream consumers. Delivery is **at-least-once**; consumers must be idempotent (Redis dedup guard). Debt/recovery/reset events are mandatory audit subjects. |
| **ADR-018** | The domain model (separate aggregates; debt dominance; reset ≠ config; no Rego-encoded quota) is canonical. The consumption path never blocks on another service. Stages 5–7 (decision/usage/debt) are one per-consumer strong-consistency transaction. |

---

## 4. Complete Use-Case Catalog

Use cases are derived one-to-one from the aggregate methods, the two saga domain services (`AllowanceEngine`, `RecoveryProcessor`), the admin service (`AllowanceAdministrationService`), and the read/resolution needs. **C** = command (mutates state, transactional), **Q** = query (read-only), **E** = event-driven use case (consumer), **J** = scheduled job trigger.

### 4.1 Policy management (config)

| # | Use case | Kind | Aggregate method | Emits |
|---|----------|------|------------------|-------|
| UC-01 | Create policy (Draft) | C | `Policy.Create` | `PolicyCreated` |
| UC-02 | Publish policy | C | `Policy.Publish` (+ `SetCompiledRego`) | `PolicyPublished` |
| UC-03 | Archive policy | C | `Policy.Archive` | `PolicyArchived` |
| UC-04 | Change policy condition/expression (Draft only) | C | `Policy.ChangeCondition` | `PolicyConditionChanged` |
| UC-05 | Get policy by id | Q | — | — |
| UC-06 | List policies by tenant | Q | — | — |

### 4.2 Subscription management (binding Policy → scope)

| # | Use case | Kind | Aggregate method | Emits |
|---|----------|------|------------------|-------|
| UC-07 | Assign subscription | C | `Subscription.Create` | `SubscriptionAssigned` |
| UC-08 | Activate subscription | C | `Subscription.Activate` | `SubscriptionActivated` |
| UC-09 | Revoke subscription | C | `Subscription.Revoke` | `SubscriptionRevoked` |
| UC-10 | Supersede subscription | C | `Subscription.Supersede` | `SubscriptionSuperseded` |
| UC-11 | Get subscription by id | Q | — | — |
| UC-12 | List subscriptions by policy | Q | — | — |
| UC-13 | Get effective subscription for scope chain | Q | `ISubscriptionResolver.ResolveAsync` | — |

### 4.3 Quota policy management (config)

| # | Use case | Kind | Aggregate method | Emits |
|---|----------|------|------------------|-------|
| UC-14 | Define quota policy | C | `QuotaPolicy.Create` | `QuotaPolicyDefined` |
| UC-15 | Amend quota policy | C | `QuotaPolicy.Amend` | `QuotaPolicyAmended` |
| UC-16 | Remove quota policy | C | `QuotaPolicy.Remove` (soft delete) | `QuotaPolicyRemoved` |
| UC-17 | Get quota policy by id | Q | — | — |
| UC-18 | List quota policies by scope | Q | — | — |
| UC-19 | Resolve effective quota for consumer | Q | `IQuotaResolver` + `IQuotaResolutionSpecification` | — |

### 4.4 Consumption (the core saga — runtime state)

| # | Use case | Kind | Domain services & aggregates | Emits |
|---|----------|------|------------------------------|-------|
| UC-20 | **Record feed consumption** (the pipeline) | C (HTTP, system) | `ISubscriptionResolver` → `IQuotaResolver` → `IAllowanceEngine.Consume(usageLedger, debtLedger, quota, action, units)` | `UsageRecorded`, plus (on excess) `DebtIncurred` + `QuotaExceeded`, plus `OperationAllowed`/`OperationDenied` |
| UC-21 | Get usage for consumer/action/window | Q | `UsageLedger.GetCount` via `IUsageLedgerRepository` | — |
| UC-22 | Get outstanding debt for consumer | Q | `DebtLedger.OutstandingDebt`/`MonthlyDebt` via `IDebtLedgerRepository` | — |
| UC-23 | Get consumer allowance status (composite: effective quota − usage − debt) | Q | resolver + both ledgers | — |

### 4.5 Recovery & administration

| # | Use case | Kind | Domain services & aggregates | Emits |
|---|----------|------|------------------------------|-------|
| UC-24 | Process window rollover / recover debt | C (triggered by J) | `IRecoveryProcessor.Recover(debtLedger, action, renewedMonthlyAllowance)` + `IRecoveryEligibilitySpecification` | `DebtRecovered` |
| UC-25 | Administrative reset (clears usage + debt; never quota policy) | C | `IAllowanceAdministrationService.Reset(usageLedger, debtLedger)` | `UsageReset`, `DebtReset` |

### 4.6 Read-model hydration (inbound integration — consumers)

| # | Use case | Kind | Source event (AuthorizationService) | Effect |
|---|----------|------|-------------------------------------|--------|
| UC-26 | Hydrate principal hierarchy — user created | E | `UserCreated` | upsert user→tenant node; lazily provision `UsageLedger`+`DebtLedger` |
| UC-27 | Hydrate principal hierarchy — role assigned to user | E | `RoleAssignedToUser` | upsert user→role edge |
| UC-28 | Hydrate principal hierarchy — user tenant changed | E | `UserTenantChanged` | move user node |
| UC-29 | Hydrate principal hierarchy — role defined | E | `RoleDefined` | upsert role→tenant node |

> **UC-26–29 are the ADR-014/ADR-018 §12–13 requirement:** resolution runs entirely against the local read model, giving zero runtime coupling to AuthorizationService availability.

### 4.7 OPA synchronization (outbound integration — consumer of own events)

| # | Use case | Kind | Trigger event | Effect |
|---|----------|------|---------------|--------|
| UC-30 | Sync compiled Rego to OPA on publish | E | `PolicyPublished` | push `RegoModule` bundle to OPA (downstream only; no quota/debt in Rego — ADR-018 §15) |
| UC-31 | Remove policy from OPA on archive | E | `PolicyArchived` | remove bundle |

**Totals:** 14 commands (UC-01/02/03/04/07/08/09/10/14/15/16/20/24/25), 11 queries (UC-05/06/11/12/13/17/18/19/21/22/23), 4 inbound consumers (UC-26–29), 2 outbound OPA consumers (UC-30/31). UC-20 (consumption) is the single hot-path saga.

> **UC-20 authorization exception:** RecordConsumption is the **only** command **not** marked `IAuthorizableRequest`. It is a system/feed operation invoked over HTTP from the feed gateway and authorized by the gateway/tenant context (ADR-013), not by ABAC policy — marking it `IAuthorizableRequest` would, under `FailClosedBehavior`, deny every consumption. It is instead marked `IIdempotentRequest`+`IRetryableRequest`, with the idempotency key supplied by the caller (the source feed event id) so at-least-once redelivery cannot double-count usage.

---

## 5. CQRS Boundaries

The service splits along a hard **write-model / read-model** line, and within writes along **config vs runtime-state**.

```
                         PolicyService.Application
   ┌───────────────────────────┬──────────────────────────┬─────────────────────┐
   │  Write model (Commands)    │  Read model (Queries)     │  Integration        │
   │                            │                           │  (Consumers/Jobs)   │
   │  Config writes:            │  Config reads:            │  Inbound:           │
   │   Policy / Subscription /  │   Get/List Policy,        │   Principal-        │
   │   QuotaPolicy commands     │   Subscription,           │   hierarchy         │
   │   (low churn, low risk)    │   QuotaPolicy             │   hydration         │
   │                            │                           │   (UC-26–29)        │
   │  Runtime-state writes:     │  Runtime reads:           │  Outbound:          │
   │   RecordConsumption (hot), │   GetUsage, GetDebt,      │   OPA sync          │
   │   Recover, Reset           │   AllowanceStatus,        │   (UC-30/31)        │
   │                            │   EffectiveQuota/Sub      │   Scheduled:        │
   │                            │   (resolution)            │   window rollover   │
   └───────────────────────────┴──────────────────────────┴─────────────────────┘
```

CQRS rules adopted for this service:

1. **Commands** implement `IRequest<Result<TResponse>>` + `ITransactionalRequest`. They mutate exactly the aggregate(s) that own the invariant and return a small response DTO (ids + resulting state), never a domain object.
2. **Queries** implement `IRequest<Result<TDto>>` and are **never** `ITransactionalRequest` (ADR-015: the UoW behavior is a no-op for queries, and queries must not assume an open transaction). Read-only queries load aggregates/read-models with `trackChanges: false`.
3. **Resolution queries** (UC-13/19) reuse the domain resolvers/specifications but expose them behind query handlers; they never mutate.
4. **Consumers** (UC-26–31) are MassTransit consumers in Infrastructure. Those that mutate local state (read-model hydration, ledger provisioning) dispatch an internal command through MediatR so they inherit the same transactional pipeline; OPA-sync consumers perform an idempotent outbound push only.

---

## 6. Command vs Query Ownership

| Owner boundary | Commands | Queries |
|----------------|----------|---------|
| **Policy** (config aggregate) | UC-01 Create, UC-02 Publish, UC-03 Archive, UC-04 ChangeCondition | UC-05 Get, UC-06 List |
| **Subscription** (binding aggregate) | UC-07 Assign, UC-08 Activate, UC-09 Revoke, UC-10 Supersede | UC-11 Get, UC-12 ListByPolicy, UC-13 ResolveEffective |
| **QuotaPolicy** (config aggregate) | UC-14 Define, UC-15 Amend, UC-16 Remove | UC-17 Get, UC-18 ListByScope, UC-19 ResolveEffective |
| **UsageLedger + DebtLedger** (runtime-state, co-transacted) | UC-20 RecordConsumption, UC-24 Recover, UC-25 Reset | UC-21 GetUsage, UC-22 GetDebt, UC-23 AllowanceStatus |
| **PrincipalHierarchyReadModel** (local projection) | UC-26–29 (via internal commands from consumers) | consumed by UC-13/19/20 resolution |
| **OPA bundle** (downstream sink) | — (no owned state) | UC-30/31 outbound push |

Ownership principle: a command mutates **one aggregate root**, except **UC-20/24/25** which coordinate `UsageLedger` + `DebtLedger` together — legitimate because ADR-018 §9 declares them a single per-consumer strong-consistency boundary, and the `AllowanceEngine`/`RecoveryProcessor`/`AllowanceAdministrationService` domain services already encapsulate that coordination purely (they receive loaded aggregates, mutate, raise events; the handler persists once).

---

## 7. Aggregate Usage Per Use Case

| Use case | Loads (repo) | Mutates | Reads-only | Domain service |
|----------|--------------|---------|------------|----------------|
| UC-01 Create policy | — | Policy (new) | — | — |
| UC-02 Publish policy | Policy | Policy | — | `IRegoGenerationService`/`IPolicyCompiler` (compile → `SetCompiledRego`) |
| UC-03 Archive policy | Policy | Policy | — | — |
| UC-04 Change condition | Policy | Policy | — | — |
| UC-07 Assign subscription | (Policy for existence) | Subscription (new) | Policy | — |
| UC-08/09/10 Sub lifecycle | Subscription | Subscription | — | — |
| UC-13 Resolve subscription | Subscription (by scope) | — | Subscription | `ISubscriptionResolver` |
| UC-14 Define quota policy | — | QuotaPolicy (new) | — | — |
| UC-15/16 Amend/Remove | QuotaPolicy | QuotaPolicy | — | — |
| UC-19 Resolve quota | QuotaPolicy (by scope list) | — | QuotaPolicy | `IQuotaResolver` + `IQuotaResolutionSpecification` |
| **UC-20 Record consumption** | UsageLedger (get-or-create), DebtLedger (get-or-create), QuotaPolicy (by scope, read-only) | UsageLedger, DebtLedger | QuotaPolicy, Subscription | `ISubscriptionResolver` → `IQuotaResolver` → `IAllowanceEngine` (uses debt-dominance & allowance-sufficiency specs internally) |
| UC-24 Recover debt | DebtLedger | DebtLedger | QuotaPolicy (renewed monthly allowance) | `IRecoveryProcessor` + `IRecoveryEligibilitySpecification` |
| UC-25 Reset | UsageLedger, DebtLedger | UsageLedger, DebtLedger | — | `IAllowanceAdministrationService` |
| UC-21/22/23 read | Usage/Debt/QuotaPolicy | — | all | (resolvers for UC-23) |
| UC-26–29 hydrate | PrincipalHierarchyReadModel | read model | — | — |
| UC-30/31 OPA | Policy (CompiledRego) | — | Policy | `IRegoGenerationService` output |

---

## 8. Conclusion

The Application layer is fully determined by the Domain surface and the six ADRs: **14 commands, 11 queries, 4 inbound consumers, 2 OPA consumers, 1 scheduled trigger**, organized as vertical feature slices (matching AuthorizationService/TenantService), all transactions owned by `UnitOfWorkBehavior`, all cross-service interaction event-driven. The single architectural hot spot is UC-20 (consumption), whose per-consumer two-ledger transaction is validated in `02-transaction-boundary-report.md`. No Application code may be written until this catalog and the sibling documents are approved.

**Next:** `01-application-execution-pipeline.md`.
