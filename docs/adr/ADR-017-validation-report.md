# Architecture Validation Report — Caching, Outbox & Messaging (Second Pass)

Date: 2026-07-10
Reviewer: Principal Architect Review Board
Method: Rigorous code inspection, cross-service reference analysis, dead code audit, claim-by-claim verification

---

## Objective

Challenge every conclusion from ADR-016 (Caching Ownership), ADR-016-outbox-audit, and ADR-017 (Messaging Architecture Audit). Separate **Verified Facts** from **Concerns** from **Recommendations**. Identify incorrect assumptions in previous audits, and assess the architecture for growth from 3 to 30 services.

---

## 1. Verified Facts (Evidence-Backed)

### 1.1 No Cross-Service Domain References

**Evidence:** Grepped `using` directives across all services. IdentityService.Infrastructure references only `IdentityService.Domain`. AuthorizationService.Infrastructure references only `AuthorizationService.Domain`. TenantService.Infrastructure references only `TenantService.Domain`. Zero cross-service Domain references exist.

**Implication:** Event contracts are fully producer-owned. Consumers receive opaque `EventEnvelope` (`JsonElement Payload`) and access properties by string name. This is a clean producer-owns pattern.

### 1.2 Outbox Dual-Repository Confirmed

Two sets of OutboxRepository exist:

| Repository | Interface | Used By | Round-Trips | Status |
|---|---|---|---|---|
| `Platform.Infrastructure.Outbox.OutboxRepository<TDbContext>` | `IPlatformOutboxRepository<TDbContext>` | `OutboxProcessorBase<TDbContext>` (the actual dispatcher) | 2 (UPDATE + SELECT) | **LIVE** |
| `IdentityService.Infrastructure.Outbox.OutboxRepository` | `IOutboxRepository` | Nothing (registered but never injected) | 1 (CTE+RETURNING) | **DEAD CODE** |
| `TenantService.Infrastructure.Outbox.OutboxRepository` | `ITenantOutboxRepository` | Nothing (not even registered in DI) | 1 (CTE+RETURNING) | **DEAD CODE** |

**Evidence:**
- `OutboxProcessorBase.cs:38` — `repository.ClaimPendingBatchAsync(...)` on `IPlatformOutboxRepository<TDbContext>`
- `IdentityService.Api\ServiceCollectionExtensions.cs:121` — `AddScoped<IOutboxRepository, OutboxRepository>()`
- `TenantService.Infrastructure\DependencyInjection.cs:87-92` — only registers `IPlatformOutboxRepository<TenantDbContext>`, never `ITenantOutboxRepository`
- No handler in Application layer injects `IOutboxRepository` (confirmed: zero matches in IdentityService.Application)

**Implication:** The optimized single-round-trip CTE+RETURNING path exists but is never reached. The two-round-trip Platform generic path is the actual production code. This is dead code masked as optimization.

### 1.3 Dead Letter Re-Poll Frequency Is 60s, Not 10s

**Evidence** (`OutboxRepository.cs:16-44`):
```sql
UPDATE "outbox_messages"
SET "NextRetryAt" = @leaseCutoff     -- NOW() + 60s
WHERE "Id" IN (
    SELECT "Id"
    FROM "outbox_messages"
    WHERE "ProcessedAt" IS NULL
      AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= NOW())  -- condition
    LIMIT @batchSize
    FOR UPDATE SKIP LOCKED
)
```

After dead letter: `ProcessedAt` stays NULL, `NextRetryAt = NOW + 60s`. The WHERE clause checks `NextRetryAt <= NOW()`, which takes 60s to become true. The poll interval (10s) only adds latency between cycles — the re-poll is governed by lease (60s).

**Previous ADR-017 claim:** "Dead letter bug means messages are polled every 10 seconds forever" — **INCORRECT**. Correct: re-polled every ~60 seconds forever.

### 1.4 Dead-Lettered Messages Have NO Cleanup

**Evidence** (`OutboxProcessorBase.cs:99-125`): `MoveToDeadLetterAsync` creates a `DeadLetterMessage` record but never calls `MarkProcessedAsync` on the original `OutboxMessage`. No scheduled cleanup exists.

**Confirmed — matches previous ADR finding.** The original outbox message retains `ProcessedAt = NULL` indefinitely, accumulating in the table.

### 1.5 `IIntegrationEvent` Is Never Implemented

**Evidence:**
- `SharedKernel\Domain\Events\IIntegrationEvent.cs` — `public interface IIntegrationEvent : IDomainEvent { string EventType { get; } }`
- Zero implementations across all 3 services (confirmed via grep)

**Status:** Dead abstraction.

### 1.6 Three IntegrationEvent Records Are Dead Code

**Evidence:**
- `AuthorizationService.Infrastructure\Messaging\IntegrationEvents\RoleAssignedIntegrationEvent.cs`
- `AuthorizationService.Infrastructure\Messaging\IntegrationEvents\PermissionGrantedIntegrationEvent.cs`
- `AuthorizationService.Infrastructure\Messaging\IntegrationEvents\AuthorizationEvaluatedIntegrationEvent.cs`

These records are never published, never consumed, and don't implement `IIntegrationEvent` or any MassTransit message interface.

**Status:** Dead code.

### 1.7 `CaptureDomainEvents` Pattern Duplicated Per-Service

- `IdentityDbContext.CaptureDomainEvents()` — private method
- `TenantDbContext.CaptureDomainEvents()` — private method
- `AuthorizationDbContext.SaveChangesAsync` override — inline logic, no helper method

Same logic written 2-3 times with minor variations.

### 1.8 `DeadLetterMessage` Entity Triplicated With Type Mismatch

| Entity | `Error` Type | `CorrelationId` Type | `MovedAt` Type |
|---|---|---|---|
| `Platform.Infrastructure.Outbox.DeadLetterMessage` | `string` (non-nullable) | `Guid?` (nullable) | `DateTimeOffset` |
| `IdentityService.Infrastructure.Persistence.DeadLetterMessage` | `string?` (nullable) | `Guid` (non-nullable) | `DateTimeOffset` |
| `AuthorizationService.Infrastructure.Persistence.DeadLetterMessage` | (same as Identity) | (same as Identity) | (same) |
| `TenantService.Infrastructure.Persistence.DeadLetterMessage` | (same as Identity) | (same as Identity) | (same) |

The configuration classes map `Platform.Infrastructure.Outbox.DeadLetterMessage`, so the service-level copies are dead code. The nullability mismatch (`Guid` vs `Guid?`) could cause serialization issues if the service copies were ever actually used.

### 1.9 `ReadinessMonitor` RabbitMQ Check Is Fake

**Evidence** (`ReadinessMonitor.cs`):
```csharp
private ReadinessComponentStatus CheckRabbitMq()
{
    return _bus is not null
        ? new ReadinessComponentStatus(true, "Configured")
        : new ReadinessComponentStatus(false, "Not configured");
}
```

No network probe. Returns "Configured" even with broker down. Compare: database check calls `CanConnectAsync()`, Redis check calls `PingAsync()`.

### 1.10 `RabbitMqMessagePublisher` Triplicated

Identical code in all 3 services. Same `IBus` injection, same header setup, same logging.

### 1.11 15 Event Types Have No Consumer

Published but never processed:
- `TenantNameUpdatedV1`, `DepartmentNameUpdatedV1`, `DepartmentDescriptionUpdatedV1`
- `PasswordChangedV1`, `PasswordResetV1`, `SessionCreatedV1`, `SessionRefreshTokenRotatedV1`
- 8 AuthorizationService events (permission lifecycle, evaluation, usage tracking)

### 1.12 Dual MassTransit in IdentityService — Confirmed, But Impact Is Uncertain

**Evidence** (both files call `services.AddMassTransit`):
- `IdentityService.Infrastructure\DependencyInjection.cs:93-132` — explicit `ReceiveEndpoint("identity.session.revocation")`, `ReceiveEndpoint("identity.tenant.cache")`
- `IdentityService.Api\Extensions\ServiceCollectionExtensions.cs:126-149` — `SetKebabCaseEndpointNameFormatter()`, `ConfigureEndpoints(context)` (auto-naming)

**What is CERTAIN:**
- `AddMassTransit` is called twice
- Consumer classes are registered twice
- Each call specifies different endpoint naming strategies (explicit vs auto)

**What is UNCERTAIN (depends on MassTransit internals):**
- Whether two separate bus instances start (Depends on whether `IHostedService` is additive or replaced)
- Whether explicit queues (`identity.session.revocation`) are actually created alongside auto-named queues
- Whether messages are delivered to both sets of queues or only one

**Previous ADR-017 claim:** "Dual registration creates two bus instances with competing consumers" — **OVERSTATED**. The exact behavior is unknown without runtime verification. The configuration is architecturally wrong regardless — two competing endpoint strategies cannot both be correct.

### 1.13 Event Naming Inconsistency

| Style | Used By | Examples |
|---|---|---|
| PascalCase | IdentityService, TenantService | `TenantCreatedV1`, `UserRegisteredV1` |
| kebab-case | AuthorizationService | `authorization.role-created.v1` |

No standard convention documented or enforced.

---

## 2. Corrected Observations (What Previous ADRs Got Wrong)

| Previous Claim | Source | Corrected Finding | Evidence |
|---|---|---|---|
| "Messages polled every 10 seconds after dead letter" | ADR-017 | Every ~60 seconds (lease duration, not poll interval) | `OutboxRepository.cs:18` — `NextRetryAt = NOW + 60s` |
| "Dual MassTransit creates two bus instances" | ADR-017 | **INCONCLUSIVE** — depends on MassTransit internals. Dual registration confirmed, runtime behavior unknown. | See 1.12 above |
| "AuthenticationService references TenantService.Domain" | ADR-017 | **INCORRECT** — no cross-service Domain references exist | Grep: zero matches across all service `.csproj` files |
| "IIntegrationEvent is a core abstraction" | (implicit) | **INCORRECT** — never implemented | Grep: zero implementations across 36 event classes |

---

## 3. Architectural Concerns

### C1. Dead-Lettered Outbox Messages Accumulate Forever
**Severity: High.** Each dead-lettered message stays in `outbox_messages` with `ProcessedAt = NULL` indefinitely. Over months of operation, this table grows unboundedly, slowing `ClaimPendingBatchAsync` queries and consuming storage.

### C2. Service-Specific OutboxRepositories Are Dead Code With a Better Algorithm
**Severity: Medium.** The single-round-trip CTE+RETURNING pattern in each service is architecturally superior to the two-round-trip Platform generic. Not only is the better implementation dead code, but the actual production code uses the less efficient version.

### C3. Dual MassTransit Creates an Unmaintainable Configuration
**Severity: High.** Two `AddMassTransit` calls with different endpoint strategies means nobody can confidently say which queues exist at runtime. Adding a new consumer requires understanding both registration points. An operational incident would be hard to diagnose.

### C4. No Schema Registry or Contract Tests
**Severity: Medium.** Event contracts are stringly-typed. Consumers access `JsonElement` by property name. A producer changing `RoleAssignedDomainEvent.RoleId` to `RoleAssignedDomainEvent.RoleIdentifier` silently breaks consumers at runtime. The `"Slug"/"slug"` fallback in `TenantCreatedCacheInvalidationConsumer.cs:30-31` proves developers already work around this fragility.

### C5. TenantStatusChangedConsumer Has No Dedup Guard
**Severity: Low.** All other 7 consumers use `IEventConsumerDeduplicationGuard`. This one doesn't. Under crash-recovery, duplicate delivery causes redundant `SaveChangesAsync` calls across all sessions for the tenant.

### C6. `DeadLetterMessage` Entity Schema Mismatch
**Severity: Low-Medium.** Platform entity defines `Error` as non-nullable `string` but service copies define `string?`. Platform `CorrelationId` is `Guid?` but service copies are `Guid`. If a migration were generated from the service-level entity, it would overwrite the Platform schema.

### C7. `OutboxMessage.MarkFailed` Uses `2^RetryCount` Seconds
**Severity: Low.** Retry 10: `2^10 = 1024` seconds = ~17 minutes. But the outbox uses `MaxAttempts = 10` (configured via `OutboxPublishPolicy`). After the 10th retry, `NextRetryAt` is set to `NOW + 1024s`. The next poll finds `RetryCount >= MaxAttempts` and moves to dead letter. The `NextRetryAt` value at retry 10 is wasted computation — the message dead-letters before that point. Minor inefficiency.

---

## 4. Cross-ADR Consistency Check

### ADR-013 vs Current Implementation

| ADR-013 Rule | Current State | Status |
|---|---|---|
| Cache ownership: slug→id belongs to IdentityService business flows | `CachedTenantServiceClient` in IdentityService, `TenantCreatedCacheInvalidationConsumer` invalidates on `TenantCreatedV1` | ✅ Consistent |
| Platform must never resolve TenantId | `PrincipalResolutionMiddleware`, `TenantMiddleware` parse JWT only, no lookup | ✅ Consistent |
| AuthorizationService never resolves TenantId | Uses `Context.TenantId` only | ✅ Consistent |
| TenantService only answers discovery queries for IdentityService | TenantService has `ResolveTenantIdBySlugAsync`, not called post-auth | ✅ Consistent |
| `ITenantResolver` / providers are dead code (noted as cleanup deferred) | Still registered, no callers | ✅ Consistent (deferred) |

**Conclusion:** ADR-013 rules are followed. No contradictions.

### ADR-017 vs Current Implementation

ADR-017's recommendations are still valid (TLS, metrics, routing, dedup, etc.). The only corrections are the frequency of dead-letter re-poll and the overstatement about dual MassTransit. No contradictions between recommendations and current code.

---

## 5. Recommendations (Priority-Ordered)

### P1. Fix Dual MassTransit Registration
Consolidate into a single `AddMassTransit` call in the Infrastructure layer. Remove the API layer registration. Use explicit endpoints for consumers that need known queue names.

### P2. Stop Dead-Letter Re-Polling
In `OutboxProcessorBase.MoveToDeadLetterAsync`, call `repository.MarkProcessedAsync(message.Id, ct)` after creating the `DeadLetterMessage` record. This prevents the original message from being re-selected on every lease cycle.

### P3. Add Dead-Letter Cleanup
Add a scheduled job (hangfire/quartz) or periodic task to purge `DeadLetterMessage` records older than N days (e.g., 30 days).

### P4. Add Dedup Guard to TenantStatusChangedConsumer
Add `IEventConsumerDeduplicationGuard` to prevent duplicate session revocation on crash-recovery.

### P5. Remove Dead Code
Delete:
- Service-specific `OutboxRepository` classes (IdentityService, TenantService)
- `IOutboxRepository` and `ITenantOutboxRepository` interfaces
- `IIntegrationEvent` interface
- 3 `*IntegrationEvent` records in AuthorizationService
- Service-local `DeadLetterMessage` entities

### P6. Platform-Consolidate Triplicated Code
Move to Platform.Infrastructure:
- `RabbitMqMessagePublisher` (one implementation, registered per service)
- `CaptureDomainEvents` pattern (base class helper or mixin) — note: this needs care since DbContext is per-service

### P7. Fix `DeadLetterMessage` Nullability
Align `Error` and `CorrelationId` types between Platform entity and service-level copies (or just delete the service-level copies as per P5).

### P8. Add Event Contract Tests
Consumer-driven contract tests per event type to validate:
- Expected properties exist in payload
- Property name casing is consistent across producer and consumer
- `envelope.Version` is handled

---

## 6. Future Ideas (3 → 30 Service Growth)

1. **Platform OutboxRepository with single-round-trip** — Replace the two-round-trip Platform generic with the CTE+RETURNING pattern that exists in the (currently dead) service-specific versions. This is the best of both worlds.

2. **Schema registry** — Central registry for event types with versioned schemas, enforced by contract tests.

3. **Broker-level routing** — Per-event-domain routing keys to avoid the all-queues-get-all-messages pattern. Currently every consumer receives every event and filters in code.

4. **Event versioning strategy** — Consumers should check `envelope.Version` and handle migration. Currently the Version field is written but never read.

5. **RabbitMQ health probe** — Replace `_bus is not null` with `IBusControl.GetProbeResult()` or a publish-receive loop.

6. **DLQ replay tooling** — Admin API endpoint to replay from `DeadLetterMessage` table back to the outbox or directly to the exchange.

7. **Per-service RabbitMQ credentials** with least-privilege permissions.

8. **TLS for RabbitMQ** — Currently PII (phone, email) in event payloads is transmitted in plaintext.

9. **Event naming standardization** — Choose kebab-case (`tenant.created.v1`, `identity.user.registered.v1`) and migrate all event types.

---

## 7. Previous ADR Score Adjustments

ADR-017 scored the architecture **4.9 / 10**. After this validation:

| Dimension | Previous Score | Adjusted Score | Reason |
|---|---|---|---|
| Topology | 4/10 | 4/10 | Unchanged — still single exchange, no broker routing |
| Reliability | 8/10 | 8/10 | Unchanged — confirms, acks, durability all good |
| Retry strategy | 6/10 | 6/10 | Unchanged |
| Dead letter handling | 3/10 | **4/10** | Re-poll is 60s not 10s (less severe). Still no cleanup or replay. |
| Message contracts | 5/10 | 6/10 | Producer-owns pattern is clean. No schema registry remains the issue. |
| Idempotency | 8/10 | 8/10 | Unchanged |
| Scalability | 6/10 | 6/10 | Unchanged |
| Security | 3/10 | 3/10 | Unchanged |
| Operations | 2/10 | 2/10 | Unchanged |
| Code quality / DRY | 4/10 | **3/10** | More dead code discovered (dual OutboxRepository, IIntegrationEvent, IntegrationEvent records, DeadLetterMessage copies, CaptureDomainEvents). Score reduced. |

**Adjusted Overall: 4.8 / 10** (slight reduction due to additional dead code findings)

---

## Appendix: File-by-File Evidence Map

| Claim | File(s) | Line(s) |
|---|---|---|
| Dual AddMassTransit | `IdentityService.Infrastructure\DependencyInjection.cs` | 93-132 |
| | `IdentityService.Api\Extensions\ServiceCollectionExtensions.cs` | 126-149 |
| OutboxRepository (Platform, 2-round-trip) | `Platform.Infrastructure\Outbox\OutboxRepository.cs` | 16-44 |
| OutboxRepository (Identity, 1-round-trip, dead) | `IdentityService.Infrastructure\Outbox\OutboxProcessor.cs` | 23-52 |
| OutboxRepository (Tenant, 1-round-trip, dead) | `TenantService.Infrastructure\Outbox\OutboxRepository.cs` | 22-51 |
| OutboxProcessorBase uses IPlatformOutboxRepository | `Platform.Infrastructure\Outbox\OutboxProcessorBase.cs` | 38, 66-67 |
| No dedup on TenantStatusChangedConsumer | `IdentityService.Infrastructure\Messaging\Consumers\TenantStatusChangedConsumer.cs` | entire file |
| "Slug"/"slug" fallback | `TenantCreatedCacheInvalidationConsumer.cs` | 30-31 |
| Fake RabbitMQ health check | `Platform.Infrastructure\Startup\ReadinessMonitor.cs` | (checkRabbitMq method) |
| IIntegrationEvent never implemented | `SharedKernel\Domain\Events\IIntegrationEvent.cs` | entire file |
| IntegrationEvent records (dead) | `AuthorizationService.Infrastructure\Messaging\IntegrationEvents\` | all 3 files |
| DeadLetterMessage type mismatch | `Platform\Platform.Infrastructure\Outbox\DeadLetterMessage.cs` | all |
| | `IdentityService.Infrastructure\Persistence\DeadLetterMessage.cs` | all |
| CaptureDomainEvents (Identity) | `IdentityService.Infrastructure\Persistence\IdentityDbContext.cs` | 161 |
| CaptureDomainEvents (Tenant) | `TenantService.Infrastructure\Persistence\TenantDbContext.cs` | 154 |
| Inline domain event capture (Authz) | `AuthorizationService.Infrastructure\Persistence\AuthorizationDbContext.cs` | 39-59 |
| No consumer for 15 event types | ADR-017 routing inventory | lines 584-588 |
| Event naming inconsistency | Across all domain events | PascalCase vs kebab-case |
