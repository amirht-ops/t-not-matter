# RabbitMQ and Messaging Architecture Audit

Date: 2026-07-09
Scope: Complete event-driven messaging across all 3 services + Platform
Method: Codebase inspection of 20+ MassTransit/consumer/publisher files

---

> **⚠️ STALE (2026-07-14).** This audit's topology claims are no longer accurate. It references a non-existent `IdentityService.Infrastructure/DependencyInjection.cs` and queue names with 0 matches; only its **Conflict A** (dual `AddMassTransit` registration / non-existent file) remains valid. Conflict B (IdentityService zero quorum/DLQ) and Conflict C (`DepartmentSoftDeletedV1` has no producer) are **RESOLVED** by Phase 1 (MSG-003 and MSG-001/002, commit `70b00d8`). For current state see `docs/reports/PRODUCTION-HARDENING-REPORT.md`.

## 1. Messaging Topology

### Exchange

A single **topic exchange** named `"security.events"` is used by all services.

| Property | Value |
|----------|-------|
| Type | topic |
| Name | `security.events` |
| Durable | true |
| Auto-delete | false |
| Configured where | Every service's `AddMassTransit` → `UsingRabbitMq`: `cfg.Publish<EventEnvelope>(p => { publish.Durable = true; publish.ExchangeType = "topic"; })` |

### Queue Inventory

| Queue Name | Service | Consumer | DLQ | DLX |
|------------|---------|----------|-----|-----|
| `identity.session.revocation` | IdentityService | `TenantStatusChangedConsumer` | `identity.session.revocation.dlq` | `identity.session.revocation.dlx` |
| `identity.tenant.cache` | IdentityService | `TenantCreatedCacheInvalidationConsumer` | `identity.tenant.cache.dlq` | `identity.tenant.cache.dlx` |
| `tenant.cache` | TenantService | `TenantCacheInvalidationConsumer` | `tenant.cache.dlq` | `tenant.cache.dlx` |
| `authorization.cache` | AuthorizationService | `AuthorizationCacheInvalidationConsumer` | `authorization.cache.dlq` | `authorization.cache.dlx` |
| `audit.pipeline` | AuthorizationService | `AuditPipelineConsumer` | `audit.pipeline.dlq` | `audit.pipeline.dlx` |
| `opa.sync` | AuthorizationService | `OpaSyncConsumer` | `opa.sync.dlq` | `opa.sync.dlx` |
| `analytics.pipeline` | AuthorizationService | `AnalyticsPipelineConsumer` | `analytics.pipeline.dlq` | `analytics.pipeline.dlx` |
| `authorization.department-soft-delete` | AuthorizationService | `DepartmentSoftDeleteConsumer` | `authorization.department-soft-delete.dlq` | `authorization.department-soft-delete.dlx` |

All queues use **quorum queues** (`SetQuorumQueue()`) for data safety.

### IdentityService Dual Registration Creates Extra Queues

IdentityService registers MassTransit **twice**:
1. `IdentityService.Infrastructure\DependencyInjection.cs` (lines 93-132) — explicit queues: `identity.session.revocation`, `identity.tenant.cache`
2. `IdentityService.Api\Extensions\ServiceCollectionExtensions.cs` (lines 126-149) — `cfg.ConfigureEndpoints(context)` auto-generates endpoint names like `TenantStatusChangedConsumer` (kebab-case). The consumer auto-registration creates extra queues that **compete** with the explicitly-defined ones from the Infrastructure layer.

This means `TenantStatusChangedConsumer` is subscribed to **two different queues**, potentially processing the same event twice — mitigated by the dedup guard but wasteful.

### Bindings (Routing Key Matching)

MassTransit topic exchange binds all queues to receive all `EventEnvelope` messages. The routing key is the event type string (e.g., `"TenantCreatedV1"`, `"authorization.role-assigned.v1"`). Each consumer **filters by event type inside the handler** — not at the binding level:

```csharp
// Example: TenantCreatedCacheInvalidationConsumer
if (envelope.EventType != "TenantCreatedV1") return;  // handler-level routing
```

This means **every consumer receives every event envelope** published to the exchange. The filtering is done in user code, not by RabbitMQ bindings. This works but is inefficient — all queues get all messages, and most consumers immediately discard them.

---

## 2. Publishers

### Who Publishes

Only the **outbox dispatcher** publishes events. There are no direct publish calls from command handlers, controllers, or application services.

### How

```
OutboxProcessorBase.ProcessMessageAsync()
    → IMessagePublisher.PublishAsync(envelope)
        → RabbitMqMessagePublisher.PublishAsync(envelope)
            → bus.Publish(envelope, ctx => {
                ctx.CorrelationId = envelope.CorrelationId;
                ctx.Headers.Set("tenant_id", envelope.TenantId.ToString("N"));
                if (envelope.CausationId.HasValue)
                    ctx.Headers.Set("causation_id", ...);
            })
```

### Publisher Type

All three `RabbitMqMessagePublisher` implementations:
- Inject `IBus` (MassTransit)
- Are registered as **Singleton** (`services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>()`)
- Publish via MassTransit's `bus.Publish` (not `ISendEndpoint` or raw RabbitMQ)
- Set `CorrelationId`, `tenant_id`, and `causation_id` headers

### Same Class Triplicated

The `RabbitMqMessagePublisher` class is an **identical copy** in each service. Same namespace pattern, same code, same headers. This is a clear candidate for consolidation into Platform.

---

## 3. Consumers

### Consumer Matrix

| Consumer | Queue | Service | Event Types Handled | Dedup | Db Write? | DB Flavor |
|----------|-------|---------|--------------------|-------|-----------|-----------|
| `TenantStatusChangedConsumer` | `identity.session.revocation` | Identity | `TenantStatusChangedV1` (disabled/suspended only) | No | Yes (`IIdentityUnitOfWork`) | EF Core |
| `TenantCreatedCacheInvalidationConsumer` | `identity.tenant.cache` | Identity | `TenantCreatedV1` | Yes (7d) | No (Redis only) | N/A |
| `TenantCacheInvalidationConsumer` | `tenant.cache` | Tenant | `TenantCreatedV1`, `TenantStatusChangedV1`, `TenantPlanUpgradedV1`, `DepartmentCreatedV1`, `DepartmentStatusChangedV1` | Yes (7d) | No | N/A |
| `AuthorizationCacheInvalidationConsumer` | `authorization.cache` | Authz | `UserRegisteredV1`, `UserLoggedInV1`, `UserActivatedV1`, `UserLockedV1`, `UserUnlockedV1`, `UserDisabledV1`, `UserDeletedV1`, `MfaEnabledV1`, `MfaVerifiedV1`, `SessionRevokedV1`, `authorization.role-assigned.v1`, `authorization.role-revoked.v1`, `authorization.permission-granted.v1`, `authorization.permission-revoked.v1` | Yes (7d) | No (Redis only) | N/A |
| `AuditPipelineConsumer` | `audit.pipeline` | Authz | **All events** (no filter) | Yes (7d) | Yes (`IAuthorizationAuditSink`) | DB |
| `AnalyticsPipelineConsumer` | `analytics.pipeline` | Authz | **All events** (no filter) | Yes (7d) | Yes (`IAnalyticsEventSink`) | DB/HTTP |
| `OpaSyncConsumer` | `opa.sync` | Authz | `authorization.role-*.v1`, `authorization.role-assigned.v1`, `authorization.role-revoked.v1`, `authorization.permission-granted.v1`, `authorization.permission-revoked.v1` | Yes (7d) | Yes (`IOpaDataUpdater`) | HTTP (OPA) |
| `DepartmentSoftDeleteConsumer` | `authorization.department-soft-delete` | Authz | `DepartmentSoftDeletedV1` | Yes (7d) | Yes (`AuthorizationDbContext`) | EF Core |

### Idempotency Coverage

5 of 8 consumers use `IEventConsumerDeduplicationGuard`. **3 do not**:
- `TenantStatusChangedConsumer` — no dedup guard. If this event is delivered twice, sessions for the tenant will be revoked twice (idempotent side-effect, but wasteful DB load).
- `DepartmentSoftDeleteConsumer` has dedup.
- `OpaSyncConsumer` has dedup.
- `AuthorizationCacheInvalidationConsumer` has dedup.

### Error Handling Pattern

Three different patterns exist:

**Pattern A — Re-throw (MassTransit retry):**
```csharp
catch (Exception ex)
{
    logger.LogError(...);
    throw;  // MassTransit retries via UseMessageRetry, eventually DLQ
}
```
Used by: `OpaSyncConsumer`, `AuditPipelineConsumer`, `AnalyticsPipelineConsumer`, `AuthorizationCacheInvalidationConsumer`, `DepartmentSoftDeleteConsumer` (some).

**Pattern B — Catch-and-return (no re-throw):**
```csharp
catch (Exception ex)
{
    logger.LogError(ex, "OPA sync failed...");
    throw;  // Same as A
}
```

**Pattern C — Not catching at all:**
`TenantStatusChangedConsumer` — no try/catch. If `SaveChangesAsync` throws, it bubbles to MassTransit which applies retry and then DLQ.

All consumers ultimately rely on MassTransit's `UseMessageRetry` + RabbitMQ dead letter queue for failure handling, since the only way to NACK a message in MassTransit is to throw an exception.

### TenantStatusChangedConsumer: No Dedup, No Try/Catch

This is the most basic consumer — it receives a `TenantStatusChangedV1`, extracts the status from the payload, and revokes all sessions for the tenant. It has no dedup guard and no error handling. If the event arrives twice, sessions are revoked twice (idempotent but wasteful).

---

## 4. Routing Strategy

### Routing Key Convention

**There is no formal routing key convention.** The routing key is the `EventType` string assigned by MassTransit from message publish. Since all messages are `EventEnvelope`, the routing behavior is:

1. All `EventEnvelope` messages are published to the `security.events` topic exchange.
2. MassTransit binds all queues to the exchange with a routing key equal to the message type name — but since all messages are `EventEnvelope`, every queue receives **every event**.
3. Consumers filter by `envelope.EventType` in code.

### EventType Naming Inconsistency

Two naming styles exist:

| Style | Examples | Source |
|-------|---------|--------|
| PascalCase (C# class-derived) | `TenantCreatedV1`, `TenantStatusChangedV1`, `UserRegisteredV1`, `UserLoggedInV1` | TenantService + IdentityService domain events |
| kebab-case (explicit string) | `authorization.role-created.v1`, `authorization.role-assigned.v1`, `authorization.permission-granted.v1` | AuthorizationService domain events |

AuthorizationService uses kebab-case event type names (`EventTypeName`), while TenantService and IdentityService use PascalCase (`EventTypeName` = C# class name).

This inconsistency means consumers must know which service produces which naming style:
- `TenantCacheInvalidationConsumer` looks for `"TenantCreatedV1"` (PascalCase)
- `AuthorizationCacheInvalidationConsumer` looks for `"authorization.role-assigned.v1"` (kebab-case)

### Finding: All Consumer Filtering Is Application-Level

Because MassTransit publishes on the same exchange with the same message type, **every queue receives every event**. Routing is entirely at the application level, not the broker level. This is suboptimal:
- Queues accumulate messages they'll never process
- Network bandwidth is wasted
- Consumer CPU is spent deserializing and filtering irrelevant messages
- True topic-based routing at the exchange level could isolate queues by event domain

---

## 5. Retry Strategy

### Publisher-Side Retry (Outbox)

The outbox dispatcher retries at the application level:
- Exponential backoff: `NextRetryAt = UtcNow + 2^RetryCount` seconds
- Max 10 attempts before dead-letter
- Each attempt goes through `ProcessMessageAsync` → `IMessagePublisher.PublishAsync`

There is **no MassTransit-level retry on the publisher side**. If `bus.Publish` throws, the exception propagates to `OutboxProcessorBase.ProcessMessageAsync`, which calls `MarkFailedAsync`.

### Consumer-Side Retry (MassTransit)

Every service configures:

```csharp
cfg.UseMessageRetry(r => r.Exponential(10,
    TimeSpan.FromSeconds(1),   // intervalMin
    TimeSpan.FromSeconds(30),  // intervalMax
    TimeSpan.FromSeconds(5))); // intervalDelta
```

This means:
- 10 retry attempts
- 1s initial interval, 30s max, 5s delta per attempt
- Applied globally to all consumers on the bus
- After 10 retries, the message is automatically dead-lettered by RabbitMQ

### Assessment

The retry strategy is reasonable for transient failures. The exponential range (1s-30s) provides enough backoff for brief RabbitMQ or network interruptions.

Potential issues:
- **No delayed retry exchange** — MassTransit is configured for immediate retry only. If a consumer fails due to a database deadlock or a downstream service being unavailable, retrying 10 times in rapid succession (1s-5s-10s-15s-20s-25s-30s-30s-30s-30s) may not be sufficient.
- **No retry differentiation** — All consumers share the same retry policy. `AuditPipelineConsumer` (logging) might not need 10 retries, while `OpaSyncConsumer` (external HTTP) might benefit from a longer-interval retry.
- **No circuit breaker** on the consumer side for downstream HTTP calls (OPA).

---

## 6. Dead Letter Handling

### RabbitMQ-Level DLQ

Every queue has a bound dead-letter exchange and queue:

```csharp
e.BindDeadLetterQueue("identity.session.revocation.dlx", "identity.session.revocation.dlq");
```

This is the RabbitMQ-native dead letter mechanism. When a message in the primary queue is NACKed (consumer throws after all retries exhausted), RabbitMQ routes it to the DLX/DLQ.

### Application-Level Dead Letter (Outbox)

Separate from RabbitMQ, the outbox dispatcher has its own dead letter mechanism:
- `DeadLetterMessage` table in PostgreSQL (one per service: `identity_dead_letter`, `tenant_dead_letter`, `authorization_dead_letter`)
- Created when an outbox message exceeds `MaxAttempts` (10) before it can be published to RabbitMQ

### Finding: Dead Letter Messages Have No Replay Mechanism

Once a message reaches either dead letter queue, there is:
- **No tooling to replay** messages from the DeadLetterMessage table back to the primary outbox
- **No tooling to replay** messages from RabbitMQ DLQ back to the primary queue
- **No monitoring** to alert on DLQ accumulation
- **No cleanup** — DLQ messages accumulate in both RabbitMQ and PostgreSQL indefinitely

### Finding: Outbox Dead Letter Bug (Confirmed from ADR-016)

The `MoveToDeadLetterAsync` method creates a `DeadLetterMessage` but **never marks the original OutboxMessage as processed or deleted**. The original message continues to be polled on every cycle, checked against `MaxAttempts`, and skipped — forever. This causes infinite polling overhead and unbounded dead-letter table growth.

---

## 7. Message Contracts

### Single Message Type

All services publish a single message type:

```csharp
// SharedKernel.Contract.Events.EventEnvelope
public sealed record EventEnvelope(
    Guid EventId,
    Guid TenantId,
    Guid CorrelationId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    JsonElement Payload,
    Guid? CausationId = null);
```

### What's Inside the Payload

The `Payload` is a `JsonElement` containing the serialized domain event. For example:

```
Payload = {
    "EventId": "...",
    "TenantId": "...",
    "CorrelationId": "...",
    "UserId": "...",
    "Username": "...",
    "TenantIdValue": "...",
    "CorrelationIdValue": "...",
    "EventTypeName": "UserRegisteredV1"
}
```

The payload includes **all properties** of the domain event record, including the metadata fields that are already in the envelope. This is redundant.

### Contract Versioning

Every event carries a `Version` field (currently all `1`). No version handling logic exists anywhere — consumers do not check `envelope.Version` or handle version migration.

### Integration Event Records (Dead Code)

`AuthorizationService.Infrastructure.Messaging.IntegrationEvents` contains 3 unused records:
- `RoleAssignedIntegrationEvent`
- `PermissionGrantedIntegrationEvent`
- `AuthorizationEvaluatedIntegrationEvent`

These do **not** implement `IIntegrationEvent`, are never serialized to the outbox, and are never published. They appear to be an abandoned attempt at a separate integration event abstraction.

### Metadata (Headers)

MassTransit headers set on publish:
- `CorrelationId` — set via MassTransit context
- `tenant_id` — custom header
- `causation_id` — custom header (optional)

---

## 8. Ordering

### Not Guaranteed. Not Needed.

The application does not rely on message ordering. Key reasons:
1. **All events are domain events** that represent completed state changes, not commands. Ordering of independent state changes is irrelevant.
2. **Cache invalidation** is idempotent — invalidating twice is harmless.
3. **OPA sync** is idempotent — the last write wins.
4. **Audit pipeline** records events independently — order doesn't matter.
5. **Session revocation** on tenant disable — processing this twice is harmless.

### Potential Ordering Concern

Within a single aggregate operation, multiple domain events can be raised (e.g., `RoleAssignedDomainEvent` and `UserLoggedInDomainEvent`). They are stored in the outbox with the same `CreatedAt` and may be processed in any order. Since the outbox processes them FIFO within a batch, and events from the same aggregate typically represent the same transaction, any ordering is semantically valid.

---

## 9. Scalability

### Competing Consumers

Quorum queues with `SKIP LOCKED`-style claiming on the outbox side enable horizontal scaling. Multiple service instances can consume from the same queue — MassTransit distributes messages across competing consumers.

### Prefetch / Concurrency Limits

There are **no explicit prefetch or concurrency limits** configured. MassTransit defaults apply:
- Prefetch count: typically `Environment.ProcessorCount * 4`
- Concurrent message limit: unlimited (each message handled on a new Task)

### Batching

No consumer-level batching is implemented. Each message is handled individually. `AuditPipelineConsumer` and `AnalyticsPipelineConsumer` write one record per event, which could be batched for efficiency.

### Assessment

Basic scalability is in place via quorum queues and competing consumers. For high-volume scenarios, the per-message (no batch) processing and application-level routing (all queues get all messages) become bottlenecks.

---

## 10. Reliability

### Publisher Confirms

All services enable publisher confirms:

```csharp
host.PublisherConfirmation = true;
```

This ensures the broker acknowledges receipt of published messages. If the broker does not confirm, the `bus.Publish` call throws, the outbox processor catches it, and retries.

### Consumer Acknowledgements

MassTransit uses automatic acknowledgement by default. When a consumer returns successfully, the message is acknowledged. When it throws, the message is NACKed and retried (up to 10 times), then dead-lettered.

### Duplicate Delivery

At-least-once delivery is inherent to the outbox pattern:
- If the outbox dispatcher publishes to RabbitMQ but crashes before marking the message processed, the message is polled again and published again.
- If RabbitMQ crashes after accepting a message but before delivering it, the consumer may see the message again after recovery.
- The dedup guard (`IEventConsumerDeduplicationGuard`) with 7-day TTL mitigates this at the consumer level.

### Network Partitions

If RabbitMQ is partitioned from a publisher:
- `bus.Publish` will throw after timeout
- Outbox processor retries with exponential backoff
- Messages remain in the database until the connection is restored

If RabbitMQ is partitioned from a consumer:
- MassTransit keeps the connection open (heartbeat)
- After heartbeat timeout, consumer reconnects
- Messages stay in the queue (quorum queues survive leader failure)

### Broker Restart

Quorum queues survive broker restarts. On restart:
- Queues are recovered
- Messages are preserved
- Consumers reconnect and continue processing from where they left off

### Cluster Readiness

No cluster-specific configuration is present. The `Host` property in `RabbitMqOptions` is a single hostname. For a RabbitMQ cluster, a connection string like `"host1,host2,host3"` would be needed (MassTransit supports this), but the current config doesn't enable it.

---

## 11. Security

### TLS

**Not configured.** The connection URL uses `rabbitmq://` (not `rabbitmqs://`). No TLS certificate configuration is present.

### Authentication

Username/password authentication is configured via `RabbitMqOptions.Username` / `RabbitMqOptions.Password`. These are validated at startup (`ValidateDataAnnotations().ValidateOnStart()`). Password is stored in configuration (appsettings / environment / secrets).

### Virtual Host

All services use `/` as the virtual host. This is the default RabbitMQ vhost. In a multi-service environment, separate virtual hosts per service or per environment would be better for isolation.

### Least Privilege

All publishing services use the same credentials (same username/password from config). There is no separation of publish vs. consume permissions, nor per-service credentials.

### Sensitive Payloads

The `EventEnvelope.Payload` is a JSON element containing the full domain event. Some events (e.g., `UserRegisteredDomainEvent`) contain PII (phone number, email). These are transmitted in plain text over RabbitMQ (no TLS). See ADR-016 audit for full PII analysis.

---

## 12. Operations

### Health Checks

The `ReadinessMonitor` has a RabbitMQ check, but **it only checks whether IBus is injected**:

```csharp
private ReadinessComponentStatus CheckRabbitMq()
{
    return _bus is not null
        ? new ReadinessComponentStatus(true, "Configured")
        : new ReadinessComponentStatus(false, "Not configured");
}
```

This does **not** verify an actual RabbitMQ connection. Compare with the database check which does `dbContext.Database.CanConnectAsync()` and the Redis check which does `db.PingAsync()`. The RabbitMQ check returns "Configured" even if the broker is down, as long as MassTransit's `IBus` was registered in DI.

### Metrics

**No messaging metrics exist.** There is no:
- Prometheus counter for published messages
- Prometheus counter for consumed messages
- Queue depth monitoring
- Consumer lag monitoring
- Message latency tracking
- DLQ count monitoring

### Management API Integration

No RabbitMQ Management HTTP API integration is present (no calls to `/api/queues`, `/api/overview`, etc.).

### Tracing / Correlation

CorrelationId is set on every message via `context.CorrelationId = envelope.CorrelationId`. This enables MassTransit's built-in tracing and logging correlation. No OpenTelemetry integration is visible.

---

# Topology Diagram

```
                    ┌──────────────────────────────────────┐
                    │         Topic Exchange               │
                    │         "security.events"            │
                    │      (durable, topic type)           │
                    └──────┬─────────────────────┬────────┘
                           │                     │
          All EventEnvelope messages            All EventEnvelope messages
          (routing by message type,             (routing by message type,
           but all are EventEnvelope)            but all are EventEnvelope)
                           │                     │
              ┌────────────┼────────────┬────────┼───────────────┐
              ▼            ▼            ▼        ▼               ▼
   ┌─────────────────┐  ┌──────────┐  ┌──────┐  ┌──────────┐  ┌──────────────┐
   │identity.session │  │identity. │  │tenant│  │authoriz. │  │audit.pipeline│
   │.revocation      │  │tenant.   │  │.cache│  │.cache    │  │              │
   │  (quorum)       │  │cache     │  │      │  │          │  │              │
   └────────┬────────┘  └─────┬────┘  └──┬───┘  └─────┬────┘  └──────┬───────┘
            ▼                  ▼          ▼            ▼              ▼
   TenantStatus    TenantCreated    TenantCache   Authorization   AuditPipeline
   ChangedConsumer CacheInvalid    Invalidation  CacheInvalid    Consumer
                   ationConsumer   Consumer      ationConsumer
                                                     │
                                                     ├── opa.sync → OpaSyncConsumer
                                                     ├── analytics.pipeline → AnalyticsPipelineConsumer
                                                     └── authorization.department-soft-delete
                                                         → DepartmentSoftDeleteConsumer

All queues receive ALL EventEnvelope messages.
Routing is handled at the application level (Consumer checks EventType).
```

---

# Publisher / Consumer Matrix

| Publish point | Sender | Message Type | Exchange | Frequency |
|---------------|--------|-------------|----------|-----------|
| IdentityService outbox → `RabbitMqMessagePublisher` | `IdentityOutboxDispatcher` | `EventEnvelope` | `security.events` | On each domain event |
| TenantService outbox → `RabbitMqMessagePublisher` | `TenantOutboxProcessor` | `EventEnvelope` | `security.events` | On each domain event |
| AuthorizationService outbox → `RabbitMqMessagePublisher` | `OutboxProcessor` | `EventEnvelope` | `security.events` | On each domain event |

| Consumer | Queue | Handles | Dedup | DB Write |
|----------|-------|---------|-------|----------|
| `TenantStatusChangedConsumer` | `identity.session.revocation` | `TenantStatusChangedV1` (subset) | No | Yes |
| `TenantCreatedCacheInvalidationConsumer` | `identity.tenant.cache` | `TenantCreatedV1` | Yes | No |
| `TenantCacheInvalidationConsumer` | `tenant.cache` | 5 Tenant/Department events | Yes | No |
| `AuthorizationCacheInvalidationConsumer` | `authorization.cache` | 14 user + authz events | Yes | No |
| `AuditPipelineConsumer` | `audit.pipeline` | All events | Yes | Yes |
| `AnalyticsPipelineConsumer` | `analytics.pipeline` | All events | Yes | Yes |
| `OpaSyncConsumer` | `opa.sync` | 8 structural authz events | Yes | Yes |
| `DepartmentSoftDeleteConsumer` | `authorization.department-soft-delete` | `DepartmentSoftDeletedV1` | Yes | Yes |

---

# Exchange / Queue Inventory

| Exchange | Type | Durable | Auto-delete | Bound Queues |
|----------|------|---------|-------------|--------------|
| `security.events` | topic | true | false | All 8 queues |
| `authorization.cache.dlx` | fanout (auto) | true | false | `authorization.cache.dlq` |
| `identity.session.revocation.dlx` | fanout (auto) | true | false | `identity.session.revocation.dlq` |
| `identity.tenant.cache.dlx` | fanout (auto) | true | false | `identity.tenant.cache.dlq` |
| `tenant.cache.dlx` | fanout (auto) | true | false | `tenant.cache.dlq` |
| `audit.pipeline.dlx` | fanout (auto) | true | false | `audit.pipeline.dlq` |
| `opa.sync.dlx` | fanout (auto) | true | false | `opa.sync.dlq` |
| `analytics.pipeline.dlx` | fanout (auto) | true | false | `analytics.pipeline.dlq` |
| `authorization.department-soft-delete.dlx` | fanout (auto) | true | false | `authorization.department-soft-delete.dlq` |

---

# Routing Key Inventory

| EventType String | Style | Produced By | Consumed By |
|-----------------|-------|------------|-------------|
| `TenantCreatedV1` | PascalCase | TenantService | `TenantCreatedCacheInvalidationConsumer`, `TenantCacheInvalidationConsumer` |
| `TenantStatusChangedV1` | PascalCase | TenantService | `TenantStatusChangedConsumer`, `TenantCacheInvalidationConsumer` |
| `TenantPlanUpgradedV1` | PascalCase | TenantService | `TenantCacheInvalidationConsumer` |
| `TenantNameUpdatedV1` | PascalCase | TenantService | None (no consumer) |
| `DepartmentCreatedV1` | PascalCase | TenantService | `TenantCacheInvalidationConsumer` |
| `DepartmentStatusChangedV1` | PascalCase | TenantService | `TenantCacheInvalidationConsumer` |
| `DepartmentNameUpdatedV1` | PascalCase | TenantService | None (no consumer) |
| `DepartmentDescriptionUpdatedV1` | PascalCase | TenantService | None (no consumer) |
| `DepartmentSoftDeletedV1` | PascalCase | TenantService | `DepartmentSoftDeleteConsumer` |
| `UserRegisteredV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `UserLoggedInV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `UserActivatedV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `UserLockedV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `UserUnlockedV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `UserDisabledV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `UserDeletedV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `MfaEnabledV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `MfaVerifiedV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `SessionRevokedV1` | PascalCase | IdentityService | `AuthorizationCacheInvalidationConsumer` |
| `PasswordChangedV1` | PascalCase | IdentityService | None |
| `PasswordResetV1` | PascalCase | IdentityService | None |
| `SessionCreatedV1` | PascalCase | IdentityService | None |
| `SessionRefreshTokenRotatedV1` | PascalCase | IdentityService | None |
| `authorization.role-created.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer` |
| `authorization.role-activated.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer` |
| `authorization.role-deactivated.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer` |
| `authorization.role-disabled.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer` |
| `authorization.role-parent-changed.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer` |
| `authorization.role-assigned.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer`, `AuthorizationCacheInvalidationConsumer` |
| `authorization.role-revoked.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer`, `AuthorizationCacheInvalidationConsumer` |
| `authorization.permission-created.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-submitted-for-review.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-approved.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-published.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-deprecated.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-archived.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-version-created.v1` | kebab-case | AuthorizationService | None |
| `authorization.permission-granted.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer`, `AuthorizationCacheInvalidationConsumer` |
| `authorization.permission-revoked.v1` | kebab-case | AuthorizationService | `OpaSyncConsumer`, `AuthorizationCacheInvalidationConsumer` |
| `authorization.evaluated.v1` | kebab-case | AuthorizationService | None |
| `authorization.usage-tracking-created.v1` | kebab-case | AuthorizationService | None |
| `authorization.usage-incremented.v1` | kebab-case | AuthorizationService | None |

**15 event types have no consumer** — they are published but never processed. These include:
- 3 TenantService events: `TenantNameUpdatedV1`, `DepartmentNameUpdatedV1`, `DepartmentDescriptionUpdatedV1`
- 3 IdentityService events: `PasswordChangedV1`, `PasswordResetV1`, `SessionCreatedV1`, `SessionRefreshTokenRotatedV1`
- 8 AuthorizationService events: `permission-created`, `permission-submitted-for-review`, `permission-approved`, `permission-published`, `permission-deprecated`, `permission-archived`, `permission-version-created`, `evaluated`, `usage-tracking-created`, `usage-incremented`

---

# Message Contract Inventory

| Contract | Location | Type | Version | Used? |
|----------|----------|------|---------|-------|
| `EventEnvelope` | `SharedKernel.Contract.Events` | Wire format | 1 | Yes — every message |
| `IDomainEvent` | `SharedKernel.Domain.Events` | Interface | N/A | Yes — 36 implementations |
| `IIntegrationEvent` | `SharedKernel.Domain.Events` | Interface | N/A | Never implemented |
| `RoleAssignedIntegrationEvent` | `AuthorizationService.Infrastructure` | Record | N/A | Dead code |
| `PermissionGrantedIntegrationEvent` | `AuthorizationService.Infrastructure` | Record | N/A | Dead code |
| `AuthorizationEvaluatedIntegrationEvent` | `AuthorizationService.Infrastructure` | Record | N/A | Dead code |

---

# Risks Ordered by Severity

| # | Risk | Severity | Impact | Mitigation |
|---|------|----------|--------|------------|
| R1 | **RabbitMQ health check doesn't actually test connectivity** | Critical | Readiness reports OK even with broker down | Replace `_bus is not null` check with actual connectivity probe (`IBusControl.GetProbeResult()` or PING) |
| R2 | **IdentityService dual MassTransit bus** | Critical | Duplicate consumers, two bus connections, auto-created queues compete with explicit ones | Consolidate into single `AddMassTransit` in Infrastructure layer |
| R3 | **No TLS for RabbitMQ** | High | Payloads including PII (phone, email) transmitted in plaintext | Configure `rabbitmqs://` with TLS certs |
| R4 | **No routing at broker level — all queues receive all messages** | High | Bandwidth waste, CPU waste, queues accumulate irrelevant messages, scaling limited | Implement proper topic routing with event domain-specific routing keys |
| R5 | **No monitoring / metrics** | High | Blind operation for queue depth, consumer lag, DLQ growth, throughput | Add Prometheus metrics via MassTransit `UsePrometheusMonitoring()` or `DiagnosticListener` |
| R6 | **No delayed retry** | High | Consumer failures from transient downstream issues retry immediately 10 times then dead-letter | Configure delayed retry exchange (MassTransit `UseDelayedRedelivery`) for transient downstream failures |
| R7 | **No replay mechanism for DLQ** | High | Dead-lettered messages cannot be replayed without manual RabbitMQ admin | Implement DLQ replay endpoint or tool |
| R8 | **15 event types have no consumer (dead code)** | Medium | Network and storage waste for published-but-unconsumed events | Audit and remove events without consumers, or add consumers |
| R9 | **Event type naming inconsistency (PascalCase vs kebab-case)** | Medium | Confusion for developers; routing key cannot be used consistently at broker level | Standardize on kebab-case (`tenant.created.v1`) for all event types |
| R10 | **3 IntegrationEvent records are dead code** | Low | Clutter | Remove unused records |
| R11 | **`TenantStatusChangedConsumer` lacks dedup guard** | Low | Potential duplicate DB writes under crash-recover | Add `IEventConsumerDeduplicationGuard` |
| R12 | **No cluster host configuration** | Low | Single point of failure for RabbitMQ | Add multi-host connection string support |
| R13 | **Same credentials for all services** | Low | No least-privilege separation between services | Use per-service credentials with scoped permissions |
| R14 | **Payload contains envelope-duplicate fields** | Low | Redundant serialization overhead in every message | Strip envelope-duplicate fields from payload |
| R15 | **`IIntegrationEvent` interface never implemented** | Low | Dead abstraction | Remove or implement |

---

# Missing Enterprise Capabilities

1. **Broker-level routing** — Use routing keys per event domain (e.g., `identity.user.registered.v1`, `tenant.department.created.v1`) so queues only receive relevant events.
2. **TLS for RabbitMQ** — All inter-service communication should be encrypted.
3. **Monitoring (Prometheus + Grafana)** — Queue depth, consumer lag, publish rate, consume rate, DLQ count, message age.
4. **Health check (real connectivity test)** — Replace `_bus is not null` with an actual RabbitMQ probe.
5. **Delayed retry exchange** — For transient consumer failures that require backoff beyond 30 seconds.
6. **DLQ replay tooling** — Ability to replay messages from RabbitMQ DLQ or PostgreSQL `DeadLetterMessage` table.
7. **Per-service RabbitMQ credentials** — Separate RabbitMQ users per service with least-privilege permissions.
8. **ConsumerDefinition classes** — Formal per-consumer configuration (retry, prefetch, concurrency, circuit breaker).
9. **Event versioning strategy** — Consumers should check `envelope.Version` and handle schema evolution.
10. **Circuit breaker on consumer HTTP calls** — OPA sync (`OpaSyncConsumer`) calls an external HTTP endpoint with no circuit breaker.
11. **Cluster connection string** — Host string should support comma-separated cluster nodes.
12. **Separate virtual hosts** — Per-environment or per-service-domain virtual hosts for isolation.
13. **Distributed tracing (OpenTelemetry)** — CorrelationId is set but no OpenTelemetry export exists.

---

# Recommended ADR Improvements

### ADR-002 (Event Driven Architecture) — Revise

- Document that **all points are unicast topic-based publish** (no direct exchanges, no routing at broker level — this is a design gap).
- Add **routing key convention**: `<service-domain>.<entity>.<action>.v<version>` (e.g., `identity.user.registered.v1`).
- Document **delivery guarantee**: At-least-once by design, exactly-once not a goal. Consumers must be idempotent.
- Document **dedup guard** as the mechanism for consumer idempotency, with fail-open behavior.
- Add **event naming convention**: All event types must be kebab-case with version suffix (e.g., `tenant.created.v1`, `authorization.role-assigned.v1`).

### New ADR: Messaging Security

- Require TLS for all RabbitMQ connections.
- Require per-service credentials with minimal required permissions.
- Require separate virtual hosts per environment (dev/staging/prod).

### New ADR: Messaging Operations

- Minimum required metrics: publish rate, consume rate, queue depth, DLQ count, message age.
- Health check must actually probe RabbitMQ connectivity.
- DLQ replay must exist (manual or automated).
- Outbox cleanup must include DLQ cleanup.

---

# Final Architecture Score

| Dimension | Score | Notes |
|-----------|-------|-------|
| Topology (exchanges, queues, bindings) | 4/10 | Single exchange, no broker-level routing, no routing keys |
| Reliability (confirms, acks, durability) | 8/10 | Publisher confirms, quorum queues, DLQs, at-least-once |
| Retry strategy | 6/10 | Exponential retry on consumer side, no delayed re-delivery, no circuit breaker on HTTP calls |
| Dead letter handling | 3/10 | DLQ exists but no replay, no monitoring, no cleanup, outbox dead letter bug |
| Message contracts | 5/10 | Single envelope type works, version field unused, PII in payload, duplicate metadata |
| Idempotency | 8/10 | 5/8 consumers have dedup, 7-day TTL, fail-open |
| Scalability | 6/10 | Competing consumers, quorum queues, no prefetch tuning, no batch processing |
| Security | 3/10 | No TLS, same credentials, plaintext PII, default vhost |
| Operations | 2/10 | Fake health check, no metrics, no tracing export, no DLQ monitoring |
| Code quality / DRY | 4/10 | RabbitMqOptions × 3, RabbitMqMessagePublisher × 3, no Platform consolidation |

**Overall: 4.9 / 10**

The messaging infrastructure has a solid foundation (Transactional Outbox -> MassTransit -> RabbitMQ) but falls short on **operations, security, topology design, and DRY principles**. The single-exchange/no-routing-keys approach works at low volume but will not scale. The fake RabbitMQ health check means the platform is blind to broker outages. TLS is missing for a security platform handling PII-bearing events.