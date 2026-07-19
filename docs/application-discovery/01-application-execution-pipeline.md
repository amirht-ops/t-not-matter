# Policy Service — Application Execution Pipeline

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00-application-discovery-report.md`.
- **Authoritative inputs:** ADR-012, ADR-013, ADR-014, ADR-015, ADR-017, ADR-018; the domain `07-policy-execution-pipeline.md`.

> **Purpose:** define the step-by-step Application-layer execution flow for every major use case: repository loading order, domain-service invocation order, aggregate interaction sequence, event generation points, transaction boundaries, UnitOfWork ownership (ADR-015), Outbox generation, Audit integration, and OPA synchronization. The domain pipeline (`07-...`) is the *runtime sequence inside the aggregates*; this document is the *orchestration contract inside the handlers* that drives it.

---

## 1. Canonical MediatR Pipeline (every command)

Registration order in `Platform.Behaviors.AddPlatformBehaviors` (outermost first). The Application layer does not re-implement any of these; it only marks requests with the right marker interfaces.

```
HTTP endpoint (TenantId already on RequestContext — ADR-012/013)
      ↓
MediatR.Send(command)
      ↓
TenantBehavior          — asserts RequestContext.TenantId present
      ↓
ValidationBehavior      — runs FluentValidation validators; short-circuits Result.ValidationFailure
      ↓
AuthorizationBehavior   — enforces IAuthorizableRequest (Action/Resource) if present
      ↓
RetryBehavior           — IRetryableRequest only
      ↓
UnitOfWorkBehavior      — BeginTransaction ─┐  (ITransactionalRequest only)
      ↓                                     │
  Command Handler (PURE)                    │  one transaction
      ↓                                     │
UnitOfWorkBehavior      — on success: SaveChangesAsync → Commit
                          on failure/throw: Rollback ──┘
      ↓
(DbContext.SaveChangesAsync materializes Outbox rows in the SAME transaction)
      ↓
LoggingBehavior / PerformanceBehavior / (IdempotencyBehavior if IIdempotentRequest)
```

**Handler purity contract (ADR-015):** the handler resolves `IRequestContextAccessor.Context` for `TenantId`/`CorrelationId`/`UserId`, loads aggregates via repositories, calls aggregate/domain-service methods, calls `repo.AddAsync`/`UpdateAsync`, and returns `Result<T>`. It never touches transaction or persistence lifecycle. Persistence happens exactly once, after the handler, in `UnitOfWorkBehavior`.

---

## 2. UC-20 — Record Feed Consumption (the core saga)

This is the only multi-aggregate, multi-service-domain-service flow. It is the Application realization of the domain `07-policy-execution-pipeline.md` stages.

### 2.1 Sequence

```
Command: RecordConsumptionCommand(ConsumerId, ActionKey, Units, ResourceScope..., IdempotencyKey)
  marker: ITransactionalRequest, IIdempotentRequest, IRetryableRequest
  trigger: HTTP POST from the feed gateway (must return a synchronous allow/deny decision)
  NOT IAuthorizableRequest — consumption is a SYSTEM operation authorized at the gateway /
           tenant context (ADR-013/018), not by ABAC policy (FailClosed would deny it otherwise)
  context: TenantId, CorrelationId, UserId  ← IRequestContextAccessor

[UnitOfWorkBehavior.BeginTransaction]                              ← tx opens (ADR-015)
  Handler.Handle:
    1. tenantId = ctx.TenantId; correlationId = ctx.CorrelationId
     2. candidateScopes = build [User, Role(s), Tenant] from PrincipalHierarchyReadModel   (read model, ADR-014)
          └─ if no hierarchy node yet (brand-new tenant) → fall back to Tenant-only scope (resilience; see 09 weakness #2)
    3. subscription = await ISubscriptionResolver.ResolveAsync(tenantId, candidateScopes) (Stage 1, read-only)
         └─ if failure → return Result.Failure (tx rolls back, nothing persisted)
    4. (optional) policy evaluation via IPolicyEvaluator on resolved policy            (Stage 2, pure)
    5. quotaPolicy = await IQuotaResolver.ResolveAsync(tenantId, candidateScopes)       (Stage 3, read-only, trackChanges:false)
    6. usageLedger = await IUsageLedgerRepository.GetOrCreateAsync(tenantId, consumerId) (Stage 4a, TRACKED)
    7. debtLedger  = await IDebtLedgerRepository.GetOrCreateAsync(tenantId, consumerId)  (Stage 4b, TRACKED)
    8. decision = IAllowanceEngine.Consume(usageLedger, debtLedger,                      (Stages 5–7, PURE)
                     quotaPolicy.Quota, actionKey, units, correlationId)
         ├─ engine records usage on usageLedger        → UsageRecorded raised
         ├─ engine incurs debt on debtLedger if excess → DebtIncurred + QuotaExceeded raised
         ├─ engine applies debt dominance (monthly)     (IDebtDominanceSpecification)
         └─ engine marks allowed/denied                → OperationAllowed / OperationDenied raised
    9. await IUsageLedgerRepository.UpdateAsync(usageLedger)      (register mutation; no save)
   10. await IDebtLedgerRepository.UpdateAsync(debtLedger)        (register mutation; no save)
   11. return Result.Success(RecordConsumptionResponse(decision, counts, debt))
[UnitOfWorkBehavior: success → SaveChangesAsync]                  ← Stage 8/9
      └─ DbContext.SaveChangesAsync override:
           • scan tracked AggregateRoots (usageLedger, debtLedger)
           • materialize 1 OutboxMessage per raised domain event (SAME tx)
           • base.SaveChangesAsync (ledger rows + outbox rows atomic)
           • ClearDomainEvents after base save succeeds
[UnitOfWorkBehavior: Commit]
      ─── asynchronous from here ───
   OutboxProcessor → RabbitMQ → { AuditService, Analytics, (edge notifications) }   (Stage 10)
   OPA sync: NOT triggered by consumption (quota/debt never in Rego — ADR-018 §15)   (Stage 11 = no-op for UC-20)
```

### 2.2 Loading order (strict)

| Order | Repository | Tracking | Why this order |
|-------|-----------|----------|----------------|
| 1 | `PrincipalHierarchyReadModel` | read-only | need scope chain before resolving anything |
| 2 | `ISubscriptionResolver` (→ `ISubscriptionRepository`) | read-only | policy binding gates the operation |
| 3 | `IQuotaResolver` (→ `IQuotaPolicyRepository`) | read-only | effective limits are reference data (no lock) |
| 4 | `IUsageLedgerRepository.GetOrCreateAsync` | **tracked** | mutated in this tx |
| 5 | `IDebtLedgerRepository.GetOrCreateAsync` | **tracked** | mutated in this tx |

Rationale: read-only reference loads (1–3) happen first and can fail fast without touching mutable state; the two tracked ledgers (4–5) are loaded last, immediately before the pure engine call, minimizing the window they are held in the change tracker.

### 2.3 Consistency

- **Strong (single tx):** UsageLedger + DebtLedger (+ their outbox rows). This is the ADR-018 §9 per-consumer boundary.
- **Eventual / read-only:** Subscription, QuotaPolicy, PrincipalHierarchyReadModel (resolved by id/scope; no lock; ADR-018 §9).
- **Downstream (never blocks):** Audit, Analytics via Outbox. OPA not involved.

---

## 3. UC-02 — Publish Policy (config + Rego generation)

```
Command: PublishPolicyCommand(PolicyId)  [ITransactionalRequest, IAuthorizableRequest]
[BeginTransaction]
  1. policy = await IPolicyRepository.GetByIdAsync(tenantId, policyId, trackChanges:true)
       └─ null → Result.Failure(NotFound)
  2. rego = IRegoGenerationService.Generate(policy.Expression, policy.Condition)   (pure; ADR-003)
       └─ failure → Result.Failure (rollback)
  3. policy.SetCompiledRego(rego.Value)          → MarkUpdated
  4. policy.Publish(correlationId)               → PolicyPublished raised
       └─ invalid lifecycle → Result.Failure (rollback)
  5. await IPolicyRepository.UpdateAsync(policy)
  return Result.Success
[SaveChanges → Outbox(PolicyPublished) → Commit]
   ─── async ───
   OutboxProcessor → RabbitMQ → OPA-sync consumer (UC-30): push RegoModule bundle to OPA
                              → AuditService (policy publication is audit-relevant)
```

Note: Rego is generated **inside** the transaction so the compiled module and the Published status commit atomically. OPA push happens **after** commit, driven by `PolicyPublished` via the outbox — never inline (OPA is downstream only, ADR-018 §15 / ADR-003).

---

## 4. UC-24 — Process Window Rollover / Recover Debt (Lazy First-Access)

```
Trigger: first request after a window renewal boundary (inside RecordConsumption via AllowanceEngine)
         — NO scheduled job / RecoveryProcessorJob (removed, ADR-018 §11b)
Path (per elapsed renewal window, inside the consumption transaction):
  1. debtLedger = loaded in AllowanceEngine.Consume (already loaded for the consumer)
  2. quotaPolicy = resolved (read-only; renewed allowance per window)
  3. if debtLedger.IsRecoveryDue(actionKey, window, boundaryStart):
        eligible = IRecoveryEligibilitySpecification.IsEligible(debtLedger.OutstandingDebt(actionKey))
          └─ not eligible → skip
        IRecoveryProcessor.Recover(debtLedger, actionKey, window, renewedAllowance, boundaryEnd, correlationId)
          → DebtLedger.Recover → DebtRecovered raised + RecoveryPosition(window) persisted
  4. await IDebtLedgerRepository.UpdateAsync(debtLedger)   (same UnitOfWorkBehavior tx as usage)
[SaveChanges → Outbox(DebtRecovered) → Commit]
   ─── async ─── AuditService (recovery is a mandatory audit subject, ADR-018 §14)
```

Recovery is debt-first and applies to the SINGLE per-(action) debt; any window's renewal repays it.
The transaction scope is per-consumer, atomic with the consumption (ADR-015). There is no
cross-consumer sweep and no watermark.

---

## 5. UC-25 — Administrative Reset

```
Command: ResetAllowanceCommand(ConsumerId)  [ITransactionalRequest, IAuthorizableRequest, IIdempotentRequest]
[BeginTransaction]
  1. usageLedger = await IUsageLedgerRepository.GetByIdAsync(tenantId, ..., trackChanges:true)
  2. debtLedger  = await IDebtLedgerRepository.GetByIdAsync(tenantId, ..., trackChanges:true)
  3. IAllowanceAdministrationService.Reset(usageLedger, debtLedger, correlationId)
       → UsageLedger.Reset → UsageReset;  DebtLedger.Reset → DebtReset
       (QuotaPolicy is NEVER loaded or touched — invariant 9 / ADR-018 §8)
  4. UpdateAsync both ledgers
  return Result.Success
[SaveChanges → Outbox(UsageReset, DebtReset) → Commit]
   ─── async ─── AuditService (reset is a mandatory audit subject)
```

Safety by construction: the reset handler has **no dependency** on `IQuotaPolicyRepository`, so it is structurally incapable of mutating configuration.

---

## 6. Config Command Template (UC-01/03/04/07/08/09/10/14/15/16)

All single-aggregate config commands share one shape:

```
Command: XCommand(...)  [ITransactionalRequest, IAuthorizableRequest, (IIdempotentRequest for create/destructive)]
[BeginTransaction]
  1. (mutating existing) aggregate = await IXRepository.GetByIdAsync(tenantId, id, trackChanges:true)
        └─ null → Result.Failure(NotFound)
     (creating) build value objects (Result-returning factories); on any failure → Result.Failure
  2. result = aggregate.Method(...args, correlationId)     → domain event raised
        └─ result.IsFailure → return result (rollback)
  3. await IXRepository.AddAsync/UpdateAsync(aggregate)
  return Result.Success(response)
[SaveChanges → Outbox(event) → Commit → (async) AuditService]
```

---

## 7. Query Template (UC-05/06/11/12/13/17/18/19/21/22/23)

```
Query: XQuery(...)  : IRequest<Result<XDto>>   (NOT ITransactionalRequest)
  Handler:
    1. tenantId = ctx.TenantId
    2. load via repository with trackChanges:false   (or resolver/spec for UC-13/19/23)
    3. map aggregate/read-model → DTO
    return Result.Success(dto)
  (UnitOfWorkBehavior is a no-op; no transaction; no outbox; no audit)
```

UC-23 (allowance status) composes: resolve effective quota (UC-19 path) + `UsageLedger.GetCount` + `DebtLedger.OutstandingDebt`, all read-only, returning a computed DTO. This is a computation read model (see `05-cache-strategy-report.md`).

---

## 8. Inbound Consumer Flow (UC-26–29 — read-model hydration)

```
RabbitMQ (AuthorizationService event, e.g. RoleAssignedToUser)
  → PolicyService MassTransit consumer (Infrastructure)
      1. dedup: IEventConsumerDeduplicationGuard.TryBeginProcessingAsync(name, eventId, 7d)  (ADR-017)
           └─ already processed → return
      2. dispatch internal command: UpsertPrincipalEdgeCommand(...) via MediatR
           └─ [ITransactionalRequest] → UnitOfWorkBehavior → PrincipalHierarchyReadModel repo → SaveChanges
      3. (UC-26 only) also dispatch ProvisionConsumerLedgersCommand → get-or-create UsageLedger + DebtLedger
```

Consumers never mutate state directly; they translate integration events into internal commands so read-model writes inherit the same transactional + outbox pipeline.

---

## 9. Outbound OPA Flow (UC-30/31 — consumer of own events)

```
RabbitMQ (own PolicyPublished / PolicyArchived, via outbox)
  → OpaSyncConsumer (Infrastructure)
      1. dedup guard
      2. load policy.CompiledRego (or bundle removal on archive)
       3. IOpaDataUpdater.SyncPolicyDataAsync(documentPath, regoModule, ct)   (idempotent HTTP PUT /v1/data/{path}; ADR-003)
  (No PolicyService state mutation. No quota/debt in Rego — ADR-018 §15.)
```

---

## 10. Event Generation Points (summary)

| Use case | Events generated | Materialized to Outbox at | Consumed by |
|----------|------------------|---------------------------|-------------|
| UC-01 | PolicyCreated | SaveChanges (same tx) | (read model) |
| UC-02 | PolicyPublished | SaveChanges | OPA sync (UC-30), Audit |
| UC-03 | PolicyArchived | SaveChanges | OPA remove (UC-31), Audit |
| UC-04 | PolicyConditionChanged | SaveChanges | Audit |
| UC-07–10 | Subscription* | SaveChanges | Audit |
| UC-14–16 | QuotaPolicy* | SaveChanges | (read model), Audit |
| UC-20 | UsageRecorded, DebtIncurred, QuotaExceeded, OperationAllowed/Denied | SaveChanges (same tx) | Audit, Analytics |
| UC-24 | DebtRecovered | SaveChanges | Audit (mandatory) |
| UC-25 | UsageReset, DebtReset | SaveChanges | Audit (mandatory) |

All events carry `TenantId` + `CorrelationId` only (ADR-012). Audit is always via Outbox → RabbitMQ → AuditService; never inline (ADR-017). Compare with AuthorizationService, which uses an inline `IAuthorizationAuditSink` — see `04-integration-boundary-report.md` §Audit for the decision to standardize on the outbox path for PolicyService.

---

## 11. ADR Conformance

| ADR | Where enforced in the pipeline |
|-----|--------------------------------|
| ADR-012 | §1 context from accessor; events carry only TenantId+CorrelationId |
| ADR-013 | TenantId consumed from RequestContext; no tenant resolution anywhere |
| ADR-014 | §2 scope chain + §8 hydration from local read model; no runtime AuthorizationService call |
| ADR-015 | §1 UnitOfWorkBehavior sole tx owner; handlers pure; outbox in SaveChanges |
| ADR-017 | §9/§10 outbox → RabbitMQ → Audit; dedup guard on consumers |
| ADR-018 | §2 per-consumer two-ledger tx; §5 reset ≠ config; §9 no quota in Rego |

**Next:** `02-transaction-boundary-report.md`.
