# ADR-015

Title:

Unified Transaction Architecture and Outbox Materialization

Status:

Accepted / Implemented

---

# Context

Before this refactoring, the three services (AuthorizationService, IdentityService, TenantService)
each owned the transaction boundary inconsistently, based on the findings in
`docs/unitofwork-analysis/`:

- **Double-managed transactions.** IdentityService handlers opened their own transactions
  (`BeginTransactionAsync`), called `SaveChangesAsync`, `CommitTransactionAsync`, and
  `RollbackTransactionAsync` inline, duplicating the responsibility that
  `UnitOfWorkBehavior` already provided for HTTP MediatR command execution. This created two
  competing transaction owners and made atomicity dependent on each handler remembering to
  commit.
- **Risk of event loss in IdentityService.** Because IdentityService had no centralized
  transaction owner and no guaranteed outbox materialization path, domain events raised by
  aggregates could be lost: a handler could persist an aggregate without a corresponding
  outbox row, breaking the integration-event pipeline. AuthorizationService and TenantService
  already captured domain events inside their `DbContext.SaveChangesAsync` override, so the
  inconsistency between services was a correctness hazard.
- **Redundant Unit of Work interfaces.** Each service exposed its own `I*UnitOfWork` interface
  carrying a bespoke `SaveChangesAsync(IReadOnlyCollection<IDomainEvent>, CancellationToken)`
  overload that let handlers push domain events into the Unit of Work. This coupled handlers to
  transaction plumbing, defeated handler purity, and blocked a single shared pipeline behavior
  from owning persistence.

The prior `UnitOfWorkBehavior` (Platform.Behaviors) already implemented the correct contract;
it was simply not the sole owner everywhere, and handlers still reached into transaction
lifecycle methods.

---

# Decision

Adopt a single **Unified Transaction Architecture** across all three services:

## 1. Sole transaction owner — `UnitOfWorkBehavior`

`Platform.Behaviors.UnitOfWorkBehavior<TRequest, TResponse>` is the **strict, sole owner** of the
transaction boundary for every command implementing `ITransactionalRequest`.

- It begins a transaction via `ITransactionalUnitOfWork.BeginTransactionAsync` before invoking the
  next pipeline stage (`UnitOfWorkBehavior.cs:19-22`).
- On a successful result it calls `IUnitOfWork.SaveChangesAsync`, then
  `CommitTransactionAsync`; on failure or a non-success `Result<T>` it calls
  `RollbackTransactionAsync` (`UnitOfWorkBehavior.cs:29-59`).
- On any thrown exception it rolls back and clears the change tracker
  (`UnitOfWorkBehavior.cs:63-71`).

Handlers therefore never touch transaction lifecycle methods.

## 2. Pure command handlers

Command handlers are **pure**: they execute domain/application logic only. They add or mutate
aggregates through repositories but must **not** call `BeginTransactionAsync`,
`SaveChangesAsync`, `CommitTransactionAsync`, `RollbackTransactionAsync`, `ClearChangeTracker`,
or `ClearDomainEvents`. Handlers also no longer depend on service-specific Unit of Work
interfaces — those are pipeline/infrastructure dependencies only.

## 3. DbContext owns the outbox

Each service `DbContext` is the single owner of the domain-event lifecycle. Inside its
`SaveChangesAsync` override it:

1. Scans tracked `AggregateRoot` entries for pending `DomainEvents`
   (`ChangeTracker.Entries<AggregateRoot>()`).
2. Materializes one `OutboxMessage` row per domain event (`OutboxMessages.Add(...)`) **before**
   `base.SaveChangesAsync`.
3. Calls `base.SaveChangesAsync` so aggregate rows and outbox rows persist in the same
   transaction.
4. Clears captured domain events **only after** the base save succeeds.

This guarantees every persisted aggregate change carries its outbox row atomically.

## 4. Collapsed Unit of Work contracts

Each `I*UnitOfWork` (`IAuthorizationUnitOfWork`, `IIdentityUnitOfWork`, `ITenantUnitOfWork`)
now derives directly from `ITransactionalUnitOfWork` with **no extra members** — the bespoke
domain-event `SaveChangesAsync` overload is removed. The shared `UnitOfWorkBehavior` depends
only on `IUnitOfWork`/`ITransactionalUnitOfWork`, so each service registers
`IUnitOfWork -> I*UnitOfWork` and the behavior works uniformly.

## Implementation notes (verified in the current codebase)

- `UnitOfWorkBehavior` is registered for all three services: IdentityService explicitly via
  `AddBehavior(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>))`
  (`IdentityService.Api/Extensions/ServiceCollectionExtensions.cs:218`); AuthorizationService and
  TenantService via the shared `AddPlatformBehaviors` extension where `EnableUnitOfWork` defaults
  to `true` (`Platform.Behaviors/BehaviorServiceCollectionExtensions.cs:25-26`,
  `BehaviorOptions.cs:7`; called from each service's API `ServiceCollectionExtensions.cs`).
- `IUnitOfWork` is mapped to the service UoW in every service's DI
  (AuthorizationService `DependencyInjection.cs:63`, IdentityService `DependencyInjection.cs:91`
  and `ServiceCollectionExtensions.cs:131`, TenantService `DependencyInjection.cs:91`).
- All mutating commands are marked `ITransactionalRequest`, including the previously
  manual-transaction Identity commands (`Logout`, `RefreshToken`, `MfaVerify`, `EnableMfa`).
- No `Application` layer handler references `IUnitOfWork`, `ITransactionalUnitOfWork`,
  `SaveChangesAsync`, `BeginTransactionAsync`, `CommitTransactionAsync`, `ClearDomainEvents`, or
  `ClearChangeTracker`.
- Each service `DbContext` implements the capture → materialize → save → clear flow:
  - AuthorizationDbContext `AuthorizationDbContext.cs:44-67`
  - IdentityDbContext `IdentityDbContext.cs:50-58` (`CaptureDomainEvents`/`ClearCapturedDomainEvents`
    at `:161-187`)
  - TenantDbContext `TenantDbContext.cs:41-57` (same helpers at `:154-186`)

---

# Consequences

## Benefits

- **Guaranteed atomicity.** Aggregate rows and outbox rows are written in one transaction owned
  by a single pipeline behavior, eliminating double-managed transactions and partial commits.
- **At-least-once delivery.** Because outbox rows are materialized inside `SaveChangesAsync`
  alongside the aggregate, every successfully persisted domain event has a corresponding outbox
  entry that the outbox dispatcher will eventually publish. Event loss in IdentityService is
  eliminated.
- **Consistent handler model.** Handlers are uniform and testable; transaction correctness no
  longer depends on individual handler discipline.
- **Simpler contracts.** A single `ITransactionalUnitOfWork`/`IUnitOfWork` abstraction replaces
  three bespoke interfaces with domain-event overloads.

## Constraints for future developers

- Mark every command that mutates persistent state with `ITransactionalRequest`; the behavior
  is a no-op for non-transactional (query) requests.
- Never call transaction or persistence methods inside a handler. If a handler needs data
  persisted to continue, the pipeline's save after the handler is the only persistence point.
- Never pass domain events out of the aggregate/handler into the Unit of Work. Domain events are
  captured and materialized solely by the `DbContext`.
- Keep outbox materialization inside the `DbContext.SaveChangesAsync` override (capture → add
  outbox rows → `base.SaveChangesAsync` → clear) and never clear events before the base save
  succeeds.
- Register transaction behavior uniformly through `AddPlatformBehaviors` (`EnableUnitOfWork`) so
  the pipeline behavior is present; do not hand-wire `UnitOfWorkBehavior` ad hoc unless matching
  the IdentityService pattern.
- Queries and non-transactional operations must not assume an open transaction.
- The outbox dispatcher remains responsible for publishing and must preserve at-least-once
  semantics (idempotent consumers / de-duplication downstream).
