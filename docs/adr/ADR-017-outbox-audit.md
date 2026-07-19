# Outbox Pattern Architecture Audit

Date: 2026-07-09
Scope: Complete end-to-end event lifecycle across all 3 services + Platform
Method: Manual codebase inspection of 65+ files

---

## 1. Event Creation

### How Integration Events Are Created

Integration events are **not** created independently. Instead, domain events are raised on aggregates, then **automatically** converted to OutboxMessage records during `SaveChangesAsync()`.

The flow:

```
AggregateRoot.RaiseDomainEvent(IDomainEvent)
    → stored in AggregateRoot._domainEvents list
    → DbContext.SaveChangesAsync() override
        → ChangeTracker.Entries<AggregateRoot>() where DomainEvents.Count > 0
        → For each IDomainEvent:
            → Serialize domain event to JSON (Payload)
            → Create OutboxMessage { Id, TenantId, CorrelationId, EventType, Payload, ... }
            → Add to DbSet<OutboxMessage>
        → base.SaveChangesAsync() (persists domain + outbox in same transaction)
        → ClearDomainEvents() on each aggregate
```

### Who Owns This

**Each service's DbContext owns the capture.** The `SaveChangesAsync` override is duplicated in all three DbContexts:

| Service | DbContext | File |
|---------|-----------|------|
| TenantService | `TenantDbContext` | Lines 41-57, 154-178 |
| IdentityService | `IdentityDbContext` | Lines 45-57, 161-185 |
| AuthorizationService | `AuthorizationDbContext` | Lines 39-67, 51-57, 88-94 |

### Finding: The conversion code is duplicated

Each DbContext has its own `ToOutboxMessage()` and `CaptureDomainEvents()` methods. They are structurally identical across all three services but duplicated in 3 files.

### Domain Event Types (36 total)

Three base records (one per service):
- `TenantDomainEvent` — 9 event types
- `IdentityDomainEvents` — 14 event types
- `AuthorizationDomainEvent` — 19 event types (some dead — 6 lifecycle events never consumed)

### Integration Event Records (waste)

3 standalone integration event records exist in AuthorizationService.Infrastructure (`RoleAssignedIntegrationEvent`, `PermissionGrantedIntegrationEvent`, `AuthorizationEvaluatedIntegrationEvent`). **None of these are actually used.** They do not implement `IIntegrationEvent`, are never serialized to the outbox, and serve no purpose. They are dead code.

---

## 2. Transaction Boundary

### Atomicity is Guaranteed — With One Caveat

The outbox messages are added to the same DbContext and persisted in the same `base.SaveChangesAsync()` call as the domain entities. This means:

- **Yes:** Outbox is written inside the same database transaction.
- **No:** Business data cannot commit while outbox fails (same transaction).
- **No:** Outbox cannot commit while business data rolls back (same transaction).

### How the Transaction Works

```
MediatR Command Handler
    → UnitOfWorkBehavior.BeginTransactionAsync()
        → IDbContextTransaction is started
    → Command Handler executes
    → UnitOfWorkBehavior inspects Result<T>.IsSuccess
        → If success: SaveChangesAsync()
            → All 3 DbContexts: CaptureDomainEvents() creates OutboxMessage records
            → base.SaveChangesAsync() persists everything atomically
        → If success: CommitTransactionAsync()
        → If failure: RollbackTransactionAsync()
```

### Assessment: Transaction Boundary Is Correct

The `UnitOfWorkBehavior` ensures:
1. A database transaction is opened before the handler.
2. Outbox messages are created inside the same DbContext session (same transaction).
3. If `Result<T>.IsSuccess == false`, the transaction is rolled back and no outbox messages are committed.
4. If `SaveChangesAsync` throws, the transaction is rolled back.

This is a textbook Transactional Outbox implementation.

### Edge Case: Dual MassTransit Registrations (IdentityService Only)

IdentityService has **two** MassTransit bus configurations:
- `IdentityService.Infrastructure.DependencyInjection` (lines 93-132) — Infrastructure layer
- `IdentityService.Api.Extensions.ServiceCollectionExtensions` (lines 126-149) — API layer

This creates two `IBus` instances, two bus connections, and two consumer registrations. The API-layer registration uses `cfg.ConfigureEndpoints(context)` (auto-discovery), while the Infrastructure-layer registration uses explicit `ReceiveEndpoint`. Both register `TenantStatusChangedConsumer`, so it would be subscribed twice, potentially processing the same event twice (mitigated by dedup guard, but wastes resources).

---

## 3. Outbox Schema

### OutboxMessage Entity

SharedKernel class (`SharedKernel.Infrastructure.Outbox.OutboxMessage`):

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, set from `EventId` of domain event |
| TenantId | Guid | Multi-tenant partition key |
| CorrelationId | Guid | Tracing |
| CausationId | Guid? | Optional cause chain |
| EventType | string(256) | e.g. `"TenantCreatedV1"`, `"authorization.role-assigned.v1"` |
| Payload | jsonb | Full domain event JSON |
| Version | int | Event schema version |
| CreatedAt | DateTimeOffset | Set at capture time |
| ProcessedAt | DateTimeOffset? | NULL until processed |
| NextRetryAt | DateTimeOffset? | Lease expiration or retry schedule |
| Error | string(2048)? | Last error message |
| RetryCount | int | Attempt counter |

### Table Names

| Service | Table | Schema | Index |
|---------|-------|--------|-------|
| TenantService | `tenant_outbox` | `Tenant` | Composite on (TenantId, ProcessedAt, NextRetryAt) |
| IdentityService | `identity_outbox` | `Identity` | Composite on (TenantId, ProcessedAt, NextRetryAt) |
| AuthorizationService | `authorization_outbox` | `authz` | Composite on (TenantId, ProcessedAt, NextRetryAt) |

### Assessment: Schema Is Production-Grade

Strengths:
- JSONB payload for flexible schema evolution
- Composite index on (TenantId, ProcessedAt, NextRetryAt) supports the common query: "WHERE ProcessedAt IS NULL AND NextRetryAt IS NULL OR NextRetryAt <= NOW()"
- MaxAttempts tracking via RetryCount
- Exponential backoff via NextRetryAt
- TenantId column enables cross-tenant queries and pagination

Weaknesses:
- No explicit `RequestId` column (uses CorrelationId as proxy)
- No `EventTypeName` discriminator on Payload (relies on EventType string)
- No partition key hint for large tables
- No cleanup/retention policy (see Section 10)

---

## 4. Dispatcher

### Architecture

All three services use the same `OutboxProcessorBase<TDbContext>` abstract class:

```
BackgroundService
    └── OutboxProcessorBase<TDbContext>
            ├── TenantOutboxProcessor : OutboxProcessorBase<TenantDbContext>
            ├── IdentityOutboxDispatcher : OutboxProcessorBase<IdentityDbContext>
            └── OutboxProcessor : OutboxProcessorBase<AuthorizationDbContext>
```

### Polling

- Interval: 10 seconds (configurable via `OutboxPublishPolicy.PollIntervalMilliseconds`)
- Batch size: 50 (`OutboxPublishPolicy.BatchSize`)
- Lease duration: 60 seconds (`OutboxPublishPolicy.LeaseDurationSeconds`)
- Max retries: 10 (`OutboxPublishPolicy.MaxAttempts`)

### Claiming Algorithm

The `OutboxRepository<TDbContext>` uses PostgreSQL `FOR UPDATE SKIP LOCKED`:

```sql
UPDATE "{{schema}}"."{{tableName}}"
SET "NextRetryAt" = @leaseCutoff
WHERE "Id" IN (
    SELECT "Id"
    FROM "{{schema}}"."{{tableName}}"
    WHERE "ProcessedAt" IS NULL
      AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= NOW())
    ORDER BY "CreatedAt"
    LIMIT @batchSize
    FOR UPDATE SKIP LOCKED
)
RETURNING ...;
```

### Critical Finding: Two Different Claiming Implementations Exist

**The Platform.Generic repository** (`Platform.Infrastructure.Outbox.OutboxRepository<TDbContext>`) uses:
- Two-step: UPDATE with FOR UPDATE SKIP LOCKED (via raw SQL), then SELECT (via LINQ for the same rows)

**The IdentityService and TenantService legacy repositories** use:
- Single-step: UPDATE ... FROM cte ... RETURNING (one round-trip, all columns returned via `FromSqlRaw` + `RETURNING`)

However, **IdentityOutboxDispatcher** and **TenantOutboxProcessor** extend `OutboxProcessorBase<TDbContext>` which uses `IPlatformOutboxRepository<TDbContext>`. The DI registration for both services (lines 89 and 89 respectively) registers `OutboxRepository<TDbContext>` (the Platform generic), **NOT** the service-specific `OutboxRepository`. The service-specific `OutboxRepository` classes (which implement `IOutboxRepository` / `ITenantOutboxRepository`) are registered but **never used by the dispatcher**.

This means the actual dispatching uses the Platform generic implementation, which does two round-trips instead of one. The service-specific repositories are dead code for the claiming path.

### Processing

Each message is processed sequentially within a batch. The loop is:

```csharp
foreach (var message in messages)
{
    await ProcessMessageAsync(scope, message, ct);
}
```

### Ordering

Messages are claimed in `ORDER BY "CreatedAt"` order within each batch. Because messages are claimed but not marked processed until after successful publish, ordering across batches is not guaranteed if a message takes longer to publish. If message A is claimed in batch 1 and takes 30 seconds to publish, message B (claimed in batch 2 with an earlier CreatedAt) could be processed before A completes. This is acceptable for eventual consistency.

### Cancellation & Graceful Shutdown

The `CancellationToken` (`stoppingToken`) is passed to `Task.Delay`, `ClaimPendingBatchAsync`, and `ProcessMessageAsync`. On service shutdown, `stoppingToken` will be cancelled, and the loop will exit on the next `Task.Delay` or database call. This is correct.

### Multiple Instances / Distributed Coordination

`FOR UPDATE SKIP LOCKED` handles multi-instance scenarios correctly. Each instance claims a non-overlapping batch. This is the standard PostgreSQL approach for outbox processors.

### No Dead Message Cleanup After MoveToDeadLetter

When a message exceeds `MaxAttempts`, `MoveToDeadLetterAsync` creates a `DeadLetterMessage` record but does **not remove** the original `OutboxMessage` row. The original `OutboxMessage` still has `ProcessedAt = NULL` and `NextRetryAt` set. This means:

- The message will be re-claimed in every subsequent poll cycle.
- It will hit the `RetryCount >= MaxAttempts` check each time.
- `MoveToDeadLetterAsync` checks `exists` and skips duplicate DeadLetterMessage creation.
- But the original OutboxMessage is **never marked processed or deleted**.

This is a **bug**: dead-lettered messages are polled forever, wasting database and processing resources on every poll cycle (10 seconds).

---

## 5. Publish Reliability

### What Happens If RabbitMQ Is Unavailable

`RabbitMqMessagePublisher.PublishAsync` calls `bus.Publish(envelope, ...)`. If the bus is unavailable:

- MassTransit's `UseMessageRetry` is configured with 10 retries (exponential, 1s-30s).
- The outbox processor's `ProcessMessageAsync` catches all exceptions and calls `MarkFailedAsync`.
- `MarkFailedAsync` sets `Error`, increments `RetryCount`, and sets `NextRetryAt = Now + 2^RetryCount` (exponential backoff).
- After 10 attempts, `MoveToDeadLetterAsync` creates a `DeadLetterMessage`.
- The original `OutboxMessage` remains unprocessed (see bug in Section 4).

### What Happens If Publishing Throws (non-transient)

- Exception caught in `ProcessMessageAsync`.
- `MarkFailedAsync` records error and sets retry.
- After `MaxAttempts` (10), dead lettered.
- The consumer side retry is handled by MassTransit `UseMessageRetry` (10 attempts, 1s-30s exponential).

### Retry Policy

| Layer | Retries | Backoff | Notes |
|-------|---------|---------|-------|
| Outbox processor `ProcessMessageAsync` | 0 | N/A | Single attempt; failure delegates to `MarkFailed` |
| `MarkFailed` exponential backoff | max 10 | `2^RetryCount` seconds | `NextRetryAt = UtcNow + (2^RetryCount)` |
| MassTransit `UseMessageRetry` | 10 | Exponential 1s-30s | Applied to consumer message handling |
| Consumer `Consume` method | 0 | N/A | Exceptions bubble to MassTransit retry |

### Poison Message Handling

The system has a dead letter queue (`DeadLetterMessage` table) and promotes messages after 10 failed attempts. However, the original `OutboxMessage` is never cleaned up (see Section 4 bug).

---

## 6. Delivery Guarantees

### Actual Guarantee: At Least Once

Explanation:

1. **At least once** — The outbox message is stored in the same transaction as business data. If the service crashes after `base.SaveChangesAsync()` but before `ClearDomainEvents()`, the events survive. On restart, the outbox processor will pick them up.

2. **But can be delivered more than once** — If the service crashes after `publisher.PublishAsync()` completes but before `MarkProcessedAsync()` saves, the same event will be picked up again on the next poll cycle and published again. The outbox gives **at least once** delivery.

3. **Exactly once is NOT guaranteed** — The combination of `at least once` delivery (outbox) + `at least once` consumer processing (MassTransit with retry) means events may be delivered multiple times.

4. **At most once risk** — If `MarkProcessedAsync` succeeds but the database write fails, the message is processed but not marked. It gets re-published. This is still at-least-once, not at-most-once. There is no scenario in which a message is lost without the consumer receiving it at least once, **provided the outbox write succeeds**.

### Assessment: Correct for the pattern

Transactional Outbox inherently provides **at least once** delivery. The system correctly does not claim exactly once. Consumer idempotency (via `RedisEventConsumerDeduplicationGuard`) mitigates the duplicate delivery risk.

---

## 7. Idempotency

### Consumer-Side Deduplication

Every consumer uses `IEventConsumerDeduplicationGuard` with a Redis-backed dedup guard:

```csharp
if (!await deduplicationGuard.TryBeginProcessingAsync(
    nameof(ConsumerName), envelope.EventId, TimeSpan.FromDays(7), ct))
{
    return; // Skip duplicate
}
```

The `RedisEventConsumerDeduplicationGuard` uses:
- `StringSetAsync(key, "1", ttl, When.NotExists)` — atomic SET with NX
- Key: `event-consumer:{consumerName}:{eventId:N}`
- TTL: 7 days default

### Fail-Open

On RedisException or TimeoutException, the guard returns `true` (allow processing). This means if Redis is down, duplicate events **will** be processed. The system defaults to correctness (process) over deduplication.

### Outbox Side

The outbox itself does not deduplicate. If a command is retried (e.g., via `IdempotencyBehavior`), the domain event is raised again and a new OutboxMessage is created. This is correct — the idempotency at the command level prevents duplicate commands, and the dedup at the consumer level handles duplicate events from crash-recover scenarios.

### Assessment: Idempotency Is Well-Engineered

Strengths:
- Three layers of defense: command idempotency (IIdempotentRequest), event dedup (Redis), and consumer dedup (same Redis)
- Fail-open prevents Redis from blocking event processing
- 7-day dedup window covers normal retry periods

Weaknesses:
- Redis dependency for deduplication — if Redis is down, dedup is disabled (fail-open)
- Dead code: `AuthorizationCacheInvalidationConsumer` is registered but `CacheInvalidationConsumer` uses in-process domain events that are never dispatched in-band through MassTransit (ADR-009 finding confirmed)

---

## 8. Failure Recovery

### Scenario 1: Database committed → Application crashes → Restart

```
1. Aggregate.Save() + OutboxMessage added to ChangeTracker
2. base.SaveChangesAsync() succeeds (both committed)
3. ClearDomainEvents() called
4. Application crashes immediately after step 3
5. OutboxMessage is in database, ProcessedAt = NULL, NextRetryAt = NULL
6. Service restarts
7. OutboxProcessorBase polls → finds the message
8. Publishes successfully
9. Marks processed
```

**Outcome: Correct.** No data loss. The message is still in the outbox with ProcessedAt = NULL.

### Scenario 2: Database committed → Outbox published → MarkProcessed fails → Crash

```
1. OutboxProcessorBase claims message
2. publisher.PublishAsync(envelope) succeeds
3. repository.MarkProcessedAsync(message.Id, ct) — EF Core call
4. Application crashes before MarkProcessedAsync completes
5. Message is still unprocessed (ProcessedAt = NULL)
6. Next poll cycle: message is re-claimed
7. Published again (duplicate)
```

**Outcome:** At-least-once delivery with duplicate. Consumer dedup guard prevents duplicate processing on the consumer side (assuming Redis is up).

### Scenario 3: RabbitMQ unavailable for extended period

```
1. PublishAsync throws (RabbitMQ down)
2. MarkFailedAsync records error, increments RetryCount, sets NextRetryAt
3. Exponential backoff: 2s, 4s, 8s, 16s, 32s, 64s, 128s, 256s, 512s, 1024s
4. After 10 attempts (~17 minutes max), dead-lettered
5. DeadLetterMessage created in database
```

**Outcome:** Messages are dead-lettered after ~17 minutes of RabbitMQ being down. They are preserved in the database but the original OutboxMessage is never cleaned up (see Section 4 bug).

### Scenario 4: Dispatcher crashes during batch processing

```
1. OutboxProcessorBase claims batch (FOR UPDATE SKIP LOCKED)
2. Sets NextRetryAt = UtcNow + 60s (lease)
3. Dispatcher crashes
4. After 60 seconds, the lease expires
5. Next poll cycle: WHERE "NextRetryAt" <= NOW() matches the expired lease
6. Message is re-claimed by another instance or the same instance
```

**Outcome:** Correct. Lease mechanism prevents permanent lockout.

### Scenario 5: Multiple dispatcher instances

Multiple instances of the same service compete for outbox messages. `FOR UPDATE SKIP LOCKED` ensures:
- Each message is claimed by exactly one instance.
- The lease prevents indefinite hold.
- Each instance processes its own batch in parallel.

**Outcome:** Correct. Multi-instance safe.

### Scenario 6: Redis unavailable

- Consumer dedup guard returns `true` (fail-open) — all events processed
- Cache invalidation consumers still reach `IDistributedCacheService` which also fails-open
- OPA sync still works
- Audit pipeline still works

**Outcome:** Correct. Deduction is lost but processing continues.

---

## 9. Multi-Tenant Support

### TenantId Propagation

TenantId flows through every layer of the event pipeline:

```
Aggregate.TenantId → IDomainEvent.TenantId
    → OutboxMessage.TenantId (stored in table)
    → EventEnvelope.TenantId (published via MassTransit)
    → Consumer receives envelope.TenantId
```

Headers are also set on the MassTransit publish:

```csharp
context.Headers.Set("tenant_id", envelope.TenantId.ToString("N"));
```

### CorrelationId Propagation

CorrelationId flows similarly: created at the command handler (MediatR), assigned to domain events by the handler, stored in OutboxMessage, published in EventEnvelope, received by consumer.

Consumers set `context.CorrelationId = envelope.CorrelationId` during publish, enabling MassTransit's tracing.

### RequestId

No explicit `RequestId` column in `OutboxMessage`. CorrelationId serves this purpose (they are equivalent in scope in this architecture).

### Assessment

Multi-tenant support is correct. Every event carries TenantId, and consumers use it for operation scoping (cache keys include tenant, OPA paths include tenant, session revocation is tenant-scoped).

---

## 10. Cleanup Strategy

### No Cleanup Exists

There is no:
- Scheduled job to delete processed outbox messages
- Retention policy (e.g., "keep 7 days")
- Archival mechanism
- Table partitioning by time
- VACUUM or index maintenance strategy

This is a **critical missing feature** for a production system over time.

### Growth Rate Estimate

Every domain-level write operation generates at least 1 outbox message. In AuthorizationService, a single `AssignRole` command generates one event. Idempotency commands also flow through the outbox. Over time:

- 1,000 operations/day → 365,000 rows/year per service
- 10,000 operations/day → 3.65M rows/year per service
- 100,000 operations/day → 36.5M rows/year per service

Without cleanup, the `tenant_outbox`, `identity_outbox`, and `authorization_outbox` tables will grow indefinitely, degrading `FOR UPDATE SKIP LOCKED` performance.

### DeadLetterMessage Table

`DeadLetterMessage` also has **no cleanup**. Failed messages accumulate forever.

---

## 11. Monitoring

### Current State

| Feature | Status | Details |
|---------|--------|---------|
| Logging | Present | `ILogger` used throughout Info/Debug/Error levels |
| Failed messages count | None | No metric, only per-failure log entries |
| Retry counts | Partial | NextRetryAt tracked per message, no aggregation |
| Oldest pending message | None | No alerting or dashboard |
| Health check | Partial | `ReadinessMonitor` checks RabbitMQ is configured but does not check queue depth or pending count |
| Dead letter statistics | None | DeadLetterMessage table has no alerting |
| Throughput | None | No messages-per-second metric |
| Latency | None | No time-from-creation-to-processing metric |
| Prometheus / OpenTelemetry | None | No integration visible |

### Assessment: Monitoring Is Insufficient for Production

The system will be blind to:
- Outbox backlog growth (messages not being processed)
- Dead-letter accumulation
- Consumer processing failures (beyond individual log lines)
- RabbitMQ queue depth
- End-to-end event latency

---

## 12. Security

### Payload Serialization

Domain events are serialized to JSON via `System.Text.Json` and stored as `jsonb` in PostgreSQL. The serialization includes **all properties of the domain event**, which may include sensitive data.

### Sensitive Data Risk

Events like `UserRegisteredDomainEvent` include `PhoneNumber` and `Email`. `UserLoggedInDomainEvent` includes no sensitive data. `PasswordChangedDomainEvent` contains no password hash.

No events include passwords, secrets, or tokens. This is acceptable for internal enterprise messaging, but PII (phone, email) is persisted in the outbox indefinitely (no cleanup).

### Encryption

- Payload at rest: No column-level encryption. PostgreSQL `jsonb` is stored as plain text.
- Payload in transit: RabbitMQ should use TLS (not configured in options, host uses `rabbitmq://` not `rabbitmqs://`).
- Payload serialization uses `System.Text.Json` with `JsonElement` for the envelope and full object serialization for the payload.

### Schema Evolution

Each event carries a `Version` field (currently all set to `1`). Consumers can check `envelope.Version` for schema evolution. However, no migration or backward-compatibility logic exists.

### Assessment

Acceptable for internal messaging in a security platform. Sensitive data is limited (phone, email). PII retention in the outbox should be addressed by adding cleanup.

---

# End-to-End Event Lifecycle Diagram

```
Command (e.g., AssignRoleCommand—IIdempotentRequest)
    │
    ├── IdempotencyBehavior (checks Redis: already processed?)
    │   └── Already processed → return cached response (idempotency)
    │
    ▼
UnitOfWorkBehavior.BeginTransactionAsync()
    │
    ▼
Command Handler
    │
    ├── AggregateRoot.Method() → RaiseDomainEvent()
    │   └── e.g., RoleAssignment.Assign() → RaiseDomainEvent(RoleAssignedDomainEvent)
    │       Event fields: EventId, TenantId, CorrelationId, OccurredAt, Version=1, EventTypeName
    │
    ▼
UnitOfWorkBehavior inspects Response.IsSuccess
    │
    ├── False → RollbackTransactionAsync() → exit
    │
    └── True  → SaveChangesAsync()
                    │
                    ▼
               DbContext.SaveChangesAsync() override
                    │
                    ├── CaptureDomainEvents()
                    │   ├── Iterate ChangeTracker.Entries<AggregateRoot>()
                    │   ├── For each IDomainEvent:
                    │   ├── JsonSerializer.SerializeToElement(domainEvent) → JSON string
                    │   └── OutboxMessages.Add(new OutboxMessage {
                    │           Id = EventId,
                    │           TenantId, CorrelationId, CausationId,
                    │           EventType = EventTypeName,
                    │           Payload = JSON, Version, CreatedAt
                    │       })
                    │
                    ├── base.SaveChangesAsync() ← ATOMIC commit
                    │   ├── Domain entity changes (e.g., new RoleAssignment row)
                    │   └── OutboxMessage row inserted (same transaction)
                    │
                    └── ClearDomainEvents(capturedRoots)
                    │
                    ▼
               CommitTransactionAsync()
                    │
                    ▼
               Command returns success response

    ─── NOW ASYNCHRONOUS ───

Background: OutboxProcessorBase<TDbContext>.ExecuteAsync()
    │
    ├── Every 10 seconds (configurable)
    │
    ▼
OutboxRepository.ClaimPendingBatchAsync(50, 60s lease)
    │
    ├── SQL: UPDATE ... SET NextRetryAt = leaseCutoff
    │         WHERE Id IN (SELECT Id FROM ... WHERE ProcessedAt IS NULL
    │                      AND (NextRetryAt IS NULL OR NextRetryAt <= NOW())
    │                      ORDER BY CreatedAt LIMIT 50 FOR UPDATE SKIP LOCKED)
    │
    ├── Returns claimed OutboxMessage[] from RETURNING or second SELECT
    │
    ▼
For each OutboxMessage:
    │
    ├── RetryCount >= MaxAttempts (10)?
    │   └── YES → MoveToDeadLetterAsync() → continue (BUG: original not cleaned)
    │
    ├── JsonDocument.Parse(Payload)
    │
    ├── EventEnvelope = new(envelope.Id, TenantId, CorrelationId,
    │       EventType, Version, OccurredAt, payloadElement, CausationId)
    │
    ▼
RabbitMqMessagePublisher.PublishAsync(envelope)
    │
    ├── bus.Publish(envelope, ctx => {
    │       ctx.CorrelationId = envelope.CorrelationId;
    │       ctx.Headers.Set("tenant_id", envelope.TenantId);
    │   })
    │
    ├── MassTransit retry: 10x, exponential 1s-30s
    │
    ├── Success → MarkProcessedAsync()
    │   └── OutboxMessage.ProcessedAt = UtcNow
    │
    └── Exception → MarkFailedAsync()
        ├── OutboxMessage.RetryCount++
        ├── OutboxMessage.NextRetryAt = UtcNow + 2^RetryCount
        └── OutboxMessage.Error = ex.Message

    ─── MESSAGE BUS ───

RabbitMQ exchange "security.events" (topic)
    │
    ├── routing key = event type string
    │
    ▼
MassTransit consumers (separate receive endpoints per service)

IdentityService consumers:
    ├── identity.session.revocation → TenantStatusChangedConsumer
    │   ├── DedupGuard (Redis: event-consumer:TenantStatusChangedConsumer:{eventId})
    │   └── On "disabled"/"suspended": revoke all sessions (IIdentityUnitOfWork)
    │
    └── identity.tenant.cache → TenantCreatedCacheInvalidationConsumer
        └── Remove cache entry for slug + dedup

TenantService consumer:
    └── tenant.cache → TenantCacheInvalidationConsumer
        └── Invalidate tenant/department cache keys + dedup

AuthorizationService consumers:
    ├── authorization.cache → AuthorizationCacheInvalidationConsumer
    │   └── Invalidate subject/role/permission cache + dedup
    ├── audit.pipeline → AuditPipelineConsumer
    ├── opa.sync → OpaSyncConsumer
    ├── analytics.pipeline → AnalyticsPipelineConsumer
    └── authorization.department-soft-delete → DepartmentSoftDeleteConsumer
```

---

# Outbox Maturity Assessment

| Dimension | Score (0-10) | Notes |
|-----------|--------------|-------|
| Transactional atomicity | 10 | Same transaction, same DbContext. Perfect. |
| Schema design | 8 | JSONB + composite index. Missing partition key. |
| Polling / dispatch | 7 | Sequential within batch. Good concurrency with SKIP LOCKED. Two round-trips instead of one. |
| Retry & backoff | 8 | Exponential backoff with max attempt dead-lettering. Original never cleaned. |
| Dead letter | 4 | Dead letter table exists but original OutboxMessage row is never cleaned. Infinite re-poll. |
| Multi-instance safety | 10 | FOR UPDATE SKIP LOCKED + lease. Correct. |
| Graceful shutdown | 8 | CancellationToken propagated but not to in-flight messages. |
| Monitoring | 2 | No metrics, no dashboards, no queue depth, no alerts. |
| Cleanup | 0 | No cleanup strategy at all. Infinite table growth. |
| Security | 7 | Some PII in payloads, no encryption. Acceptable for internal. |
| Idempotency | 9 | Three layers. Robust. Fail-open on Redis. |
| Schema evolution | 3 | Version field exists but never used. No backward compat logic. |

**Overall: 6.3 / 10 — Functionally correct but lacking production operational features**

---

# Risks Ordered by Severity

| # | Risk | Severity | Impact | Mitigation |
|---|------|----------|--------|------------|
| R1 | **Dead-lettered messages polled forever** | Critical | Unbounded table growth, wasted DB cycles every 10s | Fix `MoveToDeadLetterAsync` to mark the original OutboxMessage as processed |
| R2 | **No cleanup strategy** | Critical | Infinite table growth degrades FOR UPDATE SKIP LOCKED over time | Implement scheduled job to delete/archive processed messages (28-day retention) |
| R3 | **No monitoring** | High | Silent backlog growth, blind to failures | Add Prometheus metrics: pending count, oldest pending, retry count, dead letter count, publish latency |
| R4 | **IdentityService dual MassTransit registration** | High | Duplicate consumers, wasted resources, potential double processing (mitigated by dedup) | Consolidate to single MassTransit configuration in Infrastructure layer |
| R5 | **No alerting on dead letter accumulation** | High | Failed messages silently accumulate | Add alert on DeadLetterMessage count > threshold |
| R6 | **AuthorizationCacheInvalidationConsumer not wired (ADR-009)** | High | Cache invalidation events never processed | Wire consumer or remove dead code |
| R7 | **Platform generic OutboxRepository uses two round-trips** | Medium | 2x DB calls per batch vs optimized 1 round-trip | Align with Identity/Tenant pattern using CTE + RETURNING |
| R8 | **Service-specific OutboxRepository classes are dead code** | Medium | Confusing code. Same class name conflicts with Platform's. | Remove dead `TenantRepository` and Identity `OutboxRepository` classes |
| R9 | **3 IntegrationEvent records are dead code** | Low | No impact. Clutter. | Remove `RoleAssignedIntegrationEvent`, `PermissionGrantedIntegrationEvent`, `AuthorizationEvaluatedIntegrationEvent` |
| R10 | **No partition strategy for large tables** | Low | Long-term query degradation | Add time-based partitioning (monthly by CreatedAt) |
| R11 | **Payload contains PII (phone, email)** | Low | PII stored indefinitely in outbox table | Add cleanup (R2) to remove PII after retention period |
| R12 | **No graceful shutdown for in-flight publish** | Low | On shutdown, in-flight publishes are lost (already recovered by lease) | Acceptable — lease mechanism handles this |

---

# Missing Production Features

1. **Outbox message cleanup job** — Required. 28-day retention recommended.
2. **DeadLetterMessage cleanup job** — Required. 90-day retention.
3. **Prometheus / OpenTelemetry metrics** — Required. Pending count, oldest pending age, publish latency, dead letter count.
4. **Health check for outbox backlog** — Required. Endpoint to report `max_age_of_pending_messages` and `pending_count`.
5. **Alerting** — Required. On pending count > threshold or oldest pending > threshold.
6. **Time-based table partitioning** — Recommended for large volumes.
7. **Schema evolution handling** — Low priority. Version field exists but no handler logic.
8. **TLS for RabbitMQ** — Recommended. Currently `rabbitmq://` not `rabbitmqs://`.
9. **Duplicate MassTransit registration fix** — Recommended. API-layer duplicates Infrastructure-layer.

---

# Recommended ADR Improvements

To the existing ADRs, add or clarify:

### ADR-002 (Event Driven Architecture)

- Add explicit delivery guarantee: **At least once** by design.
- Document that **exactly once is not a goal**. Consumers must be idempotent.
- Document that `RedisEventConsumerDeduplicationGuard` provides the idempotency layer and fail-open behavior.

### ADR-009 (Cache Invalidation Strategy)

- Add the outbox cleanup job to the implementation plan.
- Remove or wire `AuthorizationCacheInvalidationConsumer` — it's dead.

### ADR-015 (Unified Transaction Architecture)

- Add the outbox message cleanup as a new architectural requirement.
- Document that `DeadLetterMessage` creation must also mark the original `OutboxMessage` as processed.

### New ADR: Outbox Operational Requirements

- Minimum monitoring: pending count, oldest pending, dead letter count.
- Cleanup: processed messages deleted after 28 days.
- Cleanup: dead letter messages deleted after 90 days.
- Alerting: pending > 1000, oldest pending > 5 minutes, dead letters > 100.

---

# Final Score

| Criterion | Score |
|-----------|-------|
| Correctness of outbox pattern | 8/10 |
| Transaction atomicity | 10/10 |
| Production readiness | 4/10 |
| Monitoring | 2/10 |
| Cleanup | 0/10 |
| Idempotency | 9/10 |
| Multi-tenant support | 9/10 |
| Code quality / duplication | 6/10 |

**Overall: 6/10**

The outbox pattern is correctly implemented at the transaction level. Events flow atomically from aggregate to database to message bus. The core architecture is sound.

The system is NOT production-ready due to:
1. **No cleanup** — tables grow indefinitely (Critical)
2. **Dead letter bug** — dead-lettered messages cause infinite polling (Critical)
3. **No monitoring** — blind operation (Critical)
4. **Dual MassTransit registration** in IdentityService (High)

These are fixable without architectural changes. The transaction boundary, event capture, dispatching, retry, and idempotency are correct.