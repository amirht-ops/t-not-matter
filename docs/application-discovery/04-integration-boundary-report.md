# Policy Service — Integration Boundary Report

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`, `01`, `02`, `03`.
- **Authoritative inputs:** ADR-014 (principal hierarchy), ADR-017 (outbox/messaging), ADR-018 §12–15 (OPA/no-runtime-coupling); reference `AuthorizationService.Infrastructure/Outbox/OutboxProcessor.cs`, `.../Messaging/Consumers/OpaSyncConsumer.cs`, `.../OpaClient/OpaDataUpdater.cs`, `SharedKernel/Contract/Events/EventEnvelope.cs`, `SharedKernel/Infrastructure/Outbox/OutboxMessage.cs`.

> **Purpose:** define every cross-process boundary — outbound domain events → RabbitMQ, inbound AuthorizationService events → read-model hydration, OPA bundle synchronization, and Audit delivery — and the exact mechanisms, primitives, and registration pattern copied from AuthorizationService.

---

## 1. Integration Surface at a Glance

```
                         PolicyService
   ┌───────────────────────────────────────────────┐
   │  Application (commands/queries/consumers)       │
   │        │                      │                 │
   │   Outbox rows          MassTransit consumers    │
   │        │                      │                 │
   └────────┼──────────────────────┼─────────────────┘
            │                      │
   (1) OutboxProcessor      (2) Inbound consumers
            │                      │
            ▼                      ▼
   RabbitMQ topic               RabbitMQ
   "security.events"  ──►  AuditService, Analytics,
   (EventEnvelope)          (edge notifications)
            │
            ▼
   (3) OPA-sync consumer → OPA data API (HTTP PUT /v1/data/{path})

   (4) Inbound hydration consumers ← AuthorizationService events
```

Four boundaries: **(1) outbound events**, **(2) inbound AuthorizationService events**, **(3) OPA sync**, **(4) Audit delivery** (a sub-case of outbound).

---

## 2. Outbound Events → Outbox → RabbitMQ

### 2.1 Transport

- `OutboxProcessor : OutboxProcessorBase<PolicyDbContext>` (BackgroundService, registered via `services.AddHostedService<OutboxProcessor>()` exactly like Authz). It polls `OutboxMessage`, builds an `EventEnvelope`, and publishes via `RabbitMqMessagePublisher` to the **`security.events`** topic exchange, with headers `tenant_id`, `correlation_id`, `causation_id`.
- `EventEnvelope` carries: `eventId, tenantId, correlationId, eventType, version, occurredAt, payload (JsonElement), causationId`. The `eventType` is the domain `EventTypeName` (e.g. `policy.usage-recorded.v1`).
- Delivery is **at-least-once**; the processor tracks `ProcessedAt/RetryCount/NextRetryAt` and dead-letters after threshold (ADR-017).

### 2.2 Events emitted (all carry `TenantId` + `CorrelationId` only — ADR-012)

| EventTypeName | Raised by | Consumer(s) |
|---------------|-----------|-------------|
| `policy.policy-created.v1` | UC-01 | (read model not needed) |
| `policy.policy-published.v1` | UC-02 | **OPA sync (UC-30)**, AuditService |
| `policy.policy-archived.v1` | UC-03 | **OPA remove (UC-31)**, AuditService |
| `policy.policy-condition-changed.v1` | UC-04 | AuditService |
| `policy.subscription-*` (assigned/activated/revoked/superseded) | UC-07–10 | AuditService |
| `policy.quota-policy-*` (defined/amended/removed) | UC-14–16 | AuditService |
| `policy.usage-recorded.v1` | UC-20 | AuditService, Analytics |
| `policy.debt-incurred.v1` / `policy.quota-exceeded.v1` | UC-20 | AuditService, Analytics |
| `policy.operation-allowed.v1` / `policy.operation-denied.v1` | UC-20 | AuditService |
| `policy.debt-recovered.v1` | UC-24 | **AuditService (mandatory)** |
| `policy.usage-reset.v1` / `policy.debt-reset.v1` | UC-25 | **AuditService (mandatory)** |

---

## 3. Inbound — Principal Hierarchy Hydration (UC-26–29, ADR-014)

PolicyService **never** calls AuthorizationService at runtime (`00` §3 ADR-014). It maintains a **local `PrincipalHierarchyReadModel`** hydrated from AuthorizationService's integration events.

| UC | AuthorizationService event (logical) | PolicyService effect |
|----|--------------------------------------|----------------------|
| UC-26 | `UserCreated` | upsert user→tenant node; **lazily provision `UsageLedger`+`DebtLedger`** via internal command |
| UC-27 | `RoleAssignedToUser` | upsert user→role edge |
| UC-28 | `UserTenantChanged` | move user node (delete old edges, add new) |
| UC-29 | `RoleDefined` | upsert role→tenant node |

**Exact external event type names are not yet confirmed** — TODO: read `AuthorizationService.Domain.Events` to bind the logical names above to the real `EventTypeName` strings and the `EventEnvelope.eventType` values the consumers must filter on.

**Consumer pattern (copied from Authz `OpaSyncConsumer`):**
```csharp
public sealed class PrincipalHierarchyConsumer : IConsumer<EventEnvelope>
{
    public async Task Consume(ConsumeContext<EventEnvelope> ctx)
    {
        var e = ctx.Message;
        if (!_watchSet.Contains(e.EventType)) return;            // filter
        if (!await _dedup.TryBeginProcessingAsync("hydration", e.EventId, TimeSpan.FromDays(7)))
            return;                                               // idempotent
        await _mediator.Send(new UpsertPrincipalEdgeCommand(...)); // → internal ITransactionalRequest
    }
}
```
Consumers translate integration events into **internal commands** so hydration writes inherit the same `UnitOfWorkBehavior` + outbox pipeline (`01` §8). No direct DB mutation in the consumer body.

---

## 4. OPA Synchronization (UC-30 / UC-31)

PolicyService owns ABAC/Rego (ADR-018 §15). On publish it pushes the compiled Rego bundle to OPA; on archive it removes it. **Quota/debt are never encoded in Rego** — OPA only answers "is this permitted under policy conditions"; allowance/quota/debt are enforced locally in UC-20.

**Primitive (from Authz `OpaDataUpdater`):**
```csharp
public interface IOpaDataUpdater
{
    Task SyncPolicyDataAsync(string documentPath, object data, CancellationToken ct);
}
// impl: PUT /v1/data/{documentPath}  with JSON body `data`
```

**PolicyService `OpaSyncConsumer : IConsumer<EventEnvelope>`:**
1. Filter on `policy.policy-published.v1` / `policy.policy-archived.v1` (`HashSet<string>` watch-set).
2. `IEventConsumerDeduplicationGuard.TryBeginProcessingAsync("opa-sync", eventId, 7d)` — throws on failure → MassTransit retry.
3. Load `Policy.CompiledRego` (via `IPolicyRepository.GetByIdAsync`) or, on archive, prepare a removal payload.
4. `IOpaDataUpdater.SyncPolicyDataAsync(documentPath, regoModule, ct)`.

**Document path scheme (TODO to confirm with OPA team):** `policy/<tenantId>/<policyId>` (per-policy bundle) or a single `policy/compiled` document partitioned by tenant. Default proposal: `policy/<tenantId>/<policyId>` for PUT; removal = PUT with empty/delete marker or `DELETE /v1/data/policy/<tenantId>/<policyId>`.

> Note: the domain `Policy.SetCompiledRego` raises **no** event (domain audit F-4). OPA sync is therefore driven by `PolicyPublished` (UC-30), which fires **after** `SetCompiledRego` in the same UC-02 transaction (`01` §3) — so `CompiledRego` is already populated when the consumer reads it. Consistent.

---

## 5. Audit Delivery (ADR-017)

- Audit is **outbox-driven only**: every audit-relevant event reaches AuditService via the `security.events` topic, **not** via an inline `IAuthorizationAuditSink` (which is what AuthorizationService uses — deliberately different; PolicyService standardizes on ADR-017).
- **Mandatory audit subjects** (per `00`/`01`): `policy.debt-recovered.v1` (UC-24), `policy.usage-reset.v1` + `policy.debt-reset.v1` (UC-25), and `policy.policy-published.v1`/`policy.policy-archived.v1` (config authority changes).
- No special Application code beyond ensuring the events are raised by the domain (they are) and that the outbox captures them (it does, per `03` §3).

---

## 6. MassTransit / Api Registration

In `PolicyService.Api` `Program.cs` (mirrors Authz `AddAuthorizationApi`):
```csharp
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddPlatformMiddleware(builder.Configuration);
builder.Services.AddPlatformBehaviors(builder.Configuration);   // MediatR pipeline
// MassTransit
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PrincipalHierarchyConsumer>();
    x.AddConsumer<OpaSyncConsumer>();
    x.UsingRabbitMq((ctx, cfg) => { /* topic exchange "security.events" */ });
});
builder.Services.AddHostedService<OutboxProcessor>();           // outbound dispatch
builder.Services.AddPlatformCaching(builder.Configuration);     // Redis (ADR-016)
builder.Services.AddHealthChecks().AddReadinessMonitor().AddRabbitMQ(...);
```
Consumers are registered with `AddConsumer<>` + `ConfigureConsumer` (retry/exponential backoff) exactly as Authz.

---

## 7. TODO / Open Questions

- [ ] Confirm exact AuthorizationService `EventTypeName` strings for UC-26–29 (read `AuthorizationService.Domain.Events`).
- [ ] Confirm OPA data-API document-path scheme + removal verb with the OPA/platform team.
- [ ] Confirm `RabbitMqMessagePublisher` + `OutboxProcessorBase<>` are reusable for `PolicyDbContext` (they are generic in the reference — expected yes).
- [ ] Decide whether edge "move" (UC-28) is a delete+insert or an update of the node's `TenantId`.

**Next:** `05-cache-strategy-report.md`.
