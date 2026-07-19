# Policy Service — Transaction Boundary Report

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`, `01`.
- **Authoritative inputs:** ADR-015 (unified transaction architecture), ADR-017 (outbox), ADR-018 §9 (consistency model).

> **Purpose:** state, per use case, the transaction boundary — one transaction per use case, aggregate loading, SaveChanges timing, event publication timing, and strong vs eventual consistency — under the ADR-015 rule that `UnitOfWorkBehavior` is the sole transaction owner.

---

## 1. Governing Rule (ADR-015, verbatim intent)

- `UnitOfWorkBehavior` is the **strict, sole owner** of the transaction boundary for every command implementing `ITransactionalRequest`.
- It `BeginTransactionAsync` before the handler; on a success `Result` it `SaveChangesAsync` then `CommitTransactionAsync`; on failure/non-success/throw it `RollbackTransactionAsync`.
- Handlers are **pure**: no `BeginTransaction`/`SaveChanges`/`Commit`/`Rollback`/`ClearChangeTracker`/`ClearDomainEvents`.
- `DbContext.SaveChangesAsync` materializes one `OutboxMessage` per domain event **inside the same transaction**, before `base.SaveChangesAsync`, and clears events only after it succeeds.

Consequence for PolicyService: **exactly one transaction per command, zero transactions per query**, and the outbox is always atomic with the aggregate write.

---

## 2. One Transaction Per Use Case

| Use case | Transactional? | Aggregates in the tx | SaveChanges timing | Events published (to outbox) at |
|----------|----------------|----------------------|--------------------|---------------------------------|
| UC-01 Create policy | Yes | Policy | after handler success | same tx |
| UC-02 Publish policy | Yes | Policy (Rego + status) | after handler success | same tx (PolicyPublished) |
| UC-03 Archive policy | Yes | Policy | after handler | same tx |
| UC-04 Change condition | Yes | Policy | after handler | same tx |
| UC-07 Assign subscription | Yes | Subscription (+ Policy read-only) | after handler | same tx |
| UC-08 Activate subscription | Yes | Subscription | after handler | same tx |
| UC-09 Revoke subscription | Yes | Subscription | after handler | same tx |
| UC-10 Supersede subscription | Yes | Subscription | after handler | same tx |
| UC-14 Define quota policy | Yes | QuotaPolicy | after handler | same tx |
| UC-15 Amend quota policy | Yes | QuotaPolicy | after handler | same tx |
| UC-16 Remove quota policy | Yes | QuotaPolicy (soft delete) | after handler | same tx |
| **UC-20 Record consumption** | Yes | **UsageLedger + DebtLedger** | after handler | same tx (5 possible events) |
| UC-24 Recover debt | Yes | DebtLedger | after handler | same tx (DebtRecovered) |
| UC-25 Reset | Yes | UsageLedger + DebtLedger | after handler | same tx (UsageReset, DebtReset) |
| UC-26–29 Hydrate read model | Yes (internal command) | PrincipalHierarchyReadModel | after handler | (read model — no domain events) |
| UC-05/06/11/12/13/17/18/19/21/22/23 Queries | **No** | none (read-only) | never | none |
| UC-30/31 OPA sync | No (outbound only) | none | never | none |

**Invariant:** every row in the "Transactional? Yes" set is marked `ITransactionalRequest`; every query is **not**. There is no use case that opens two transactions, and no use case that mutates state outside a transaction.

---

## 3. The Two Multi-Aggregate Transactions

Only UC-20 and UC-25 enlist two aggregates. Both are legitimate under ADR-018 §9 (per-consumer strong consistency boundary), and both operate on the **same consumer's** UsageLedger + DebtLedger in the **same store** — so this is a single local DB transaction, not a distributed one.

```
UC-20 Record consumption          UC-25 Reset
┌────────────────────────┐        ┌────────────────────────┐
│ tx { UsageLedger,      │        │ tx { UsageLedger,      │
│      DebtLedger }      │        │      DebtLedger }      │
│  strong consistency    │        │  strong consistency    │
│  per consumer          │        │  per consumer          │
└────────────────────────┘        └────────────────────────┘
QuotaPolicy read-only (no lock)    QuotaPolicy NOT loaded
Subscription read-only (no lock)
```

**Concurrency:** contention is per-consumer only (no cross-consumer debt — ADR-018 §9). Per-consumer optimistic concurrency (the `Version` column on `Entity`) is sufficient. Two concurrent consumption commands for the same consumer resolve via optimistic-concurrency retry; commands for different consumers never contend.

---

## 4. SaveChanges & Event-Publication Timing

```
t0  BeginTransaction                         (UnitOfWorkBehavior)
t1  handler mutates aggregates in memory     (domain events accumulate on aggregates)
t2  handler returns Result.Success
t3  SaveChangesAsync                          ← ONLY persistence point
      t3a  capture domain events from tracked AggregateRoots
      t3b  add OutboxMessage rows (same DbContext/tx)
      t3c  base.SaveChangesAsync  → aggregate rows + outbox rows commit-ready atomically
      t3d  ClearDomainEvents (after t3c succeeds)
t4  CommitTransaction
        ── transaction closed ──
t5+ OutboxProcessor (background, ~10s poll) claims rows → publishes to RabbitMQ
t6+ AuditService / Analytics / OPA-sync / read-model consumers process (at-least-once)
```

- **Event publication to the bus is asynchronous** and happens strictly after commit (t5+). Inside the transaction (t3) events are only *materialized to the outbox table*.
- On any failure before t4, the transaction rolls back: no aggregate rows, **no outbox rows**, no events ever reach the bus.

---

## 5. Strong vs Eventual Consistency Map

| Concern | Consistency | Rationale |
|---------|-------------|-----------|
| UsageLedger ↔ DebtLedger (same consumer) | **Strong** | single tx, ADR-018 §9; "operation succeeds + debt recorded" + monthly dominance must be atomic |
| Aggregate write ↔ its Outbox rows | **Strong** | ADR-015; same tx in `SaveChangesAsync` |
| QuotaPolicy config ↔ runtime consumption | **Eventual** | policy resolved by id/scope at read time, no lock; amendments are rare (ADR-018 §9) |
| Subscription ↔ consumption | **Eventual** | resolved read-only; binding changes propagate via events |
| PrincipalHierarchyReadModel ↔ AuthorizationService | **Eventual** | hydrated by integration events; ADR-014/018 §12–13 |
| PolicyService state ↔ OPA bundle | **Eventual** | OPA is downstream; synced post-commit via PolicyPublished (ADR-003/018 §15) |
| PolicyService state ↔ AuditService | **Eventual** | at-least-once via outbox (ADR-017) |
| Analytics, notifications | **Eventual** | downstream consumers, never block consumption |

---

## 6. Aggregate Loading Discipline (per tx)

| Rule | Applies to |
|------|-----------|
| Load read-only reference data (`trackChanges:false`) **before** tracked aggregates | UC-20 (Subscription, QuotaPolicy), UC-24 (QuotaPolicy) |
| Load mutable aggregates (`trackChanges:true`) **last**, just before the domain call | all commands |
| Never load an aggregate you will not mutate into the change tracker | reset never tracks QuotaPolicy |
| `GetOrCreateAsync` for lazily-provisioned ledgers is inside the tx (idempotent create) | UC-20 UsageLedger/DebtLedger |
| One command mutates one consumer's ledgers only | UC-20/24/25 |

---

## 7. Failure & Rollback Semantics

| Failure point | Outcome |
|---------------|---------|
| Validation fails (ValidationBehavior) | short-circuit before tx opens; no transaction |
| Authorization fails (AuthorizationBehavior) | short-circuit before tx opens |
| Resolver returns failure (UC-20 step 3/5) | handler returns `Result.Failure` → Rollback → nothing persisted |
| Aggregate method returns `Result.Failure` | handler returns it → Rollback |
| `SaveChangesAsync` throws (e.g. optimistic concurrency) | Rollback + change tracker cleared (ADR-015); command surfaces failure; caller/`IIdempotentRequest` may retry |
| Commit succeeds, outbox dispatch later fails | at-least-once retry by OutboxProcessor; business state already durable (ADR-017) |

---

## 8. Conformance Checklist

- [x] Exactly one transaction per command; zero per query.
- [x] `UnitOfWorkBehavior` is the only place SaveChanges/Commit/Rollback occur.
- [x] Outbox rows are written in the same transaction as the aggregate (never a second tx).
- [x] Multi-aggregate transactions (UC-20/25) are same-store, per-consumer, ADR-018 §9-sanctioned.
- [x] Event publication to the bus is post-commit and asynchronous.
- [x] Strong consistency is limited to the per-consumer ledger pair + its outbox; everything cross-boundary is eventual.

**Next:** `03-repository-specification-usage-report.md`.
