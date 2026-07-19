# Policy Service — Ordered Implementation Plan & TODO

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`–`07`.
- **Gating:** no Application code is written until this discovery set (00–09) is **approved**. After approval, implement strictly phase-by-phase; each phase has a validation gate.

> **Purpose:** a single ordered, ticket-ready plan that turns the discovery into buildable work, with a TODO list and the same validation gates the Domain used (build → tests → architecture audit).

---

## Phase Order (dependencies first)

```
P0  Bootstrap & wiring        (projects, DI, Program.cs, behaviors, MassTransit, outbox host)
  └─ P1  Persistence foundation (PolicyDbContext, EF mappings, repositories, UoW, outbox capture)
       └─ P2  Commands          (config UC-01..04/07..10/14..16, then UC-20/24/25)
            └─ P3  Queries      (UC-05/06/11/12/13/17/18/19/21/22/23)
                 └─ P4  Read-model hydration (UC-26..29 consumers + internal commands)
                      └─ P5  OPA sync (UC-30/31 consumer + IOpaDataUpdater wiring)
                           └─ P6  Rollover trigger (LAZY first-access in AllowanceEngine; no scheduler)
                                └─ P7  Cross-cutting (audit verify, cache invalidation, obs, tests)
                                     └─ P8  Architecture audit (mirror Domain gate)
```

---

## P0 — Bootstrap & Wiring (no domain logic)

- [ ] `PolicyService.Application` `AddApplication()`: `AddMediatR(RegisterServicesFromAssemblyContaining<XHandler>)`, `AddValidatorsFromAssemblyContaining<XHandler>()`.
- [ ] `PolicyService.Infrastructure` `AddInfrastructure(config)`: Redis cache, `AddPlatformInfrastructure`, DbContext pool (Npgsql + `TenantRlsInterceptor`), repositories, UoW, `OutboxRepository<PolicyDbContext>`, `IOpaDataUpdater` (HTTP client → OPA), `IConnectionMultiplexer`.
- [ ] `PolicyService.Api` `Program.cs`: `AddApplication()` + `AddInfrastructure()`, `AddPlatformMiddleware`, **`AddPlatformBehaviors(configuration)`**, JWT bearer, MassTransit (`AddConsumer<>` ×2 + `ConfigureConsumer`), `AddHostedService<OutboxProcessor>()`, health checks (Readiness, RabbitMQ, Redis).
- [ ] `appsettings.json` sections: `ConnectionStrings:Policy`, `Redis`, `Opa` (BaseUrl/TimeoutMs), `MassTransit`, `Behaviors`.

**Gate:** solution builds; Api starts; migrations apply (`ApplyMigrationsAsync<PolicyDbContext>`); health green.

---

## P1 — Persistence Foundation

- [ ] **R-1 spike FIRST:** prove `OwnsMany` + field access maps the three private dictionaries (`UsageLedger._counters`, `DebtLedger._debts`, `DebtLedger._recoveries`). Capture working `IEntityTypeConfiguration` samples.
- [ ] `PolicyDbContext` with `DbSet<>` for 5 aggregates + `OutboxMessage`; **`SaveChangesAsync` override** capturing `AggregateRoot.DomainEvents` → `OutboxMessage` rows (same tx, then `ClearDomainEvents`).
- [ ] 5 `IEntityTypeConfiguration` classes (composite keys, `ValueGeneratedNever`, `Ignore(DomainEvents)`, VO `HasConversion`, `Version` concurrency token).
- [ ] 5 repositories (template `03` §2) + 5 `I*UnitOfWork : ITransactionalUnitOfWork` registered as `IUnitOfWork`.
- [ ] Unique constraint on `(tenant_id, consumer_id)` for ledgers to back `GetOrCreateAsync` idempotency (R-2).

**Gate:** unit test that saving a `UsageLedger` with counters + raising `UsageRecordedDomainEvent` produces 1 ledger row + 1 outbox row in the same transaction; rollback on failure produces neither.

---

## P2 — Commands

- [ ] Config commands UC-01/02/03/04 (Policy), UC-07/08/09/10 (Subscription), UC-14/15/16 (QuotaPolicy): handler + validator + response DTO, `ITransactionalRequest`+`IAuthorizableRequest`(+idempotent for creates).
- [ ] **UC-20 Record consumption (HTTP, system):** handler invoking `ISubscriptionResolver`→`IQuotaResolver`→`IAllowanceEngine.Consume`, `GetOrCreateAsync` ledgers, `UpdateAsync` both; markers `ITransactionalRequest`+`IIdempotentRequest`+`IRetryableRequest` (NOT `IAuthorizableRequest` — gateway-authorized). Carries caller-supplied `IdempotencyKey` to prevent double-count on at-least-once redelivery.
- [ ] UC-24 Recover debt (job-dispatched command) + UC-25 Reset (admin).
- [ ] `PublishPolicyCommand` sets `CompiledRego` then `Publish` in one tx (`01` §3).

**Gate:** each command persists exactly its aggregate(s) + outbox row; rollback path verified.

---

## P3 — Queries

- [ ] UC-05/06 (policy), UC-11/12 (subscription), UC-17/18 (quota) — simple `trackChanges:false` reads → DTO.
- [ ] UC-13/19 resolution queries (`ICachedQuery`) → resolvers.
- [ ] UC-21/22 ledger reads (no cache).
- [ ] UC-23 allowance status (composite, optional 30s cache).

**Gate:** queries never open a transaction; cached queries serve from Redis on hit, fall through on miss/failure.

---

## P4 — Read-Model Hydration (UC-26–29)

- [ ] `PrincipalHierarchyReadModel` table + repository.
- [ ] `UpsertPrincipalEdgeCommand` (internal, `ITransactionalRequest`+idempotent) + handler.
- [ ] `PrincipalHierarchyConsumer : IConsumer<EventEnvelope>` (dedup guard, filter watch-set, dispatch internal command). UC-26 also provisions ledgers.

---

## P5 — OPA Sync (UC-30/31)

- [ ] `OpaSyncConsumer : IConsumer<EventEnvelope>` filtering `policy.policy-published.v1` / `policy.policy-archived.v1`; loads `CompiledRego`; `IOpaDataUpdater.SyncPolicyDataAsync(documentPath, rego, ct)`; removal on archive.
- [ ] Finalize OPA document-path scheme (TODO in `04`).

---

## P6 — Scheduled Jobs

- [x] `RecoveryProcessor` (domain service): repays the SINGLE `OutstandingDebt(action)` debt-first by any window's renewed allowance; invoked lazily by `AllowanceEngine` (no `RecoveryProcessorJob` / scheduler). Per-consumer boundary preserved.
      - TODO: define debtor enumeration — query `DebtLedger` where any `OutstandingDebt(action, window) > 0` (per `09` R-?); paginate to avoid a single giant scan.
- [ ] (Optional) confirm no separate "monthly reset" job — reset is admin-only (UC-25), distinct from recovery (ADR-018 §8).

---

## P7 — Cross-Cutting

- [ ] `CacheInvalidationConsumer` (invalidates effective-subscription/quota keys on config events).
- [ ] Verify AuditService receives mandatory events (debt/reset/recovery/publish) via outbox.
- [ ] Observability: log consumption decisions; metrics on debt-incurred rate.
- [ ] Integration + unit tests for each use case; idempotency tests for UC-20/24/25/30/31.

**Gate:** full build + test suite green.

---

## P8 — Architecture Audit (mirror Domain gate)

- [ ] Run the same architecture audit the Domain passed (`docs/domain-discovery/11-architecture-audit.md` methodology) against the Application layer.
- [ ] Produce `docs/application-discovery/10-architecture-audit.md` with verdict (target: **READY**).
- [ ] Address any non-blocking findings or record them as follow-up ADRs.

---

## Master TODO (cross-cutting)

- [ ] Confirm AuthorizationService inbound `EventTypeName` strings (`04` §3).
- [ ] Confirm OPA document-path/removal verb (`04` §4).
- [ ] Define PolicyService authorization policy set + ensure ABAC rules exist (`06` §5).
- [ ] Finalize UC-20 idempotency key scheme (`06` §2).
- [ ] R-1 EF dictionary mapping spike (`03` §4.2 / P1).
- [ ] R-2 ledger `GetOrCreateAsync` concurrency (`03` §6).

**Next:** `09-review-and-refactor.md` (critical self-review).
