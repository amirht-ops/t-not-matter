# PolicyService.Application + Infrastructure — Architecture Validation Gate

**Date:** 2026-07-15
**Scope:** `src/Services/PolicyService/PolicyService.Application` (13 commands, 11 queries, 1 internal command, DI) and `PolicyService.Infrastructure` (DbContext + 7 EF configs, 6 repositories, UoW, OPA client, 3 consumers, recovery job).
**Authoritative references:** ADR-012, ADR-013, ADR-015, ADR-017, ADR-018 + discovery docs 00–08.
**Method:** Static read of every Application/Infrastructure source file, cross-checked against the ADRs, the discovery docs, the `SharedKernel`/`Platform` primitives (`AggregateRoot`/`Entity`, MediatR pipeline behaviors, `IUnitOfWork`, `TenantRlsInterceptor`, `IEventConsumerDeduplicationGuard`), and the established conventions of `IdentityService`/`AuthorizationService`.

---

## 1. Architecture Audit Report

### Phase 1 — Command / Query Responsibility & Markers

| Use case | Type | Markers | Verdict |
|----------|------|---------|---------|
| UC-01 CreatePolicy | Command | ITransactional + IAuthorizable (+idempotent) | ✓ |
| UC-02 PublishPolicy | Command | ITransactional + IAuthorizable | ✓ (sets `CompiledRego` then `Publish` in one tx) |
| UC-03 ArchivePolicy | Command | ITransactional + IAuthorizable | ✓ |
| UC-04 ChangePolicyCondition | Command | ITransactional + IAuthorizable | ✓ |
| UC-07..10 Subscription ×4 | Command | ITransactional + IAuthorizable | ✓ |
| UC-14..16 QuotaPolicy ×3 | Command | ITransactional + IAuthorizable | ✓ |
| UC-20 RecordConsumption | Command | ITransactional + IIdempotent + IRetryable (**not** IAuthorizable) | ✓ gateway-authorized, caller-supplied idempotency key |
| UC-24 RecoverDebt | Command | ITransactional (job-dispatched) | ✓ per-consumer boundary preserved |
| UC-25 ResetAllowance | Command | ITransactional + IAuthorizable (admin) | ✓ invariant 10, no quota dep |
| UC-05/06, 11/12, 17/18 | Query | none | ✓ simple `trackChanges:false` reads |
| UC-13 ResolveEffectiveSubscription | Query | ICachedQuery (5m) | ✓ tenant-scoped key |
| UC-19 ResolveEffectiveQuota | Query | ICachedQuery (5m) | ✓ tenant-scoped key |
| UC-21/22 ledger reads | Query | none | ✓ no cache (per discovery) |
| UC-23 allowance status | Query | ICachedQuery (30s) | ✓ |
| UpsertPrincipalEdge (internal) | Command | ITransactional + IIdempotent | ✓ consumer-dispatched, carries TenantId from envelope |

**Findings**
- **No query opens a transaction** (ADR-015). All `trackChanges:false`; cached queries serve from Redis on hit. ✓
- **UC-20 is correctly NOT authorizable** — the gateway authorizes; the handler is idempotent + retryable so at-least-once redelivery cannot double-count (idempotency key = consumer+action+window). ✓
- **Marker discipline is consistent** with the platform pipeline (`UnitOfWorkBehavior` wraps `ITransactionalRequest`; `AuthorizationBehavior` requires `IAuthorizableRequest`; `IdempotencyBehavior` requires `IIdempotentRequest`; `RetryBehavior` requires `IRetryableRequest`). No command mixes markers incorrectly.

### Phase 2 — Handler Purity (ADR-015)

Every handler:
- Reads/loads aggregates via repositories (tenant-scoped), invokes **pure** domain services (`AllowanceEngine`, `RecoveryProcessor`, resolvers), and returns `Result<T>`.
- Performs **no I/O beyond repository/domain-service calls**; no HTTP, no direct `DbContext.SaveChangesAsync` (only `repository.UpdateAsync`).
- Leaves transaction + outbox capture to `UnitOfWorkBehavior` (the single tx owner) which commits `PolicyDbContext`, whose `SaveChangesAsync` override harvests `AggregateRoot.DomainEvents` → `OutboxMessage` rows.

`RecoverDebtCommandHandler` now consumes `IRecoveryEligibilitySpecification.IsEligible` (resolving one of the Domain's F-1 orphaned specs as the single source of truth). `IDebtDominanceSpecification` / `IAllowanceSufficiencySpecification` remain unused (carried from Domain F-1; the engine's inline logic is currently consistent).

### Phase 3 — Repository & Idempotency Audit

- **Tenant-scoped signatures** (`GetByIdAsync(tenantId, id, …)`, `GetByConsumerIdAsync(tenantId, consumerId, …)`) match the `Identity`/`Authorization` convention and feed the global tenant/soft-delete filter. ✓
- **`GetOrCreateAsync`** for ledgers backs UC-20/UC-26 idempotency; unique constraint on `(tenant_id, consumer_id)` (R-2 in Domain) prevents duplicate ledgers under concurrency. ✓
- **`IPrincipalHierarchyRepository`** provides `GetOrCreateNodeAsync`/`GetOrCreateEdgeAsync`/`RemoveEdgeAsync` + `ListEdgesByUserAsync`; the read model is persisted via the same `PolicyDbContext` (new `principal_nodes`/`principal_edges` tables) and benefits from the global filter. ✓
- **`IDebtLedgerRepository.ListDebtorsAsync`** (P6) enumerates ledgers with outstanding debt, paginated; relies on the caller establishing a tenant-bypassing request context (cross-tenant scan). ✓

### Phase 4 — Caching Audit (cache report §2)

- Cached queries (`UC-13`, `UC-19`, `UC-23`) implement `ICachedQuery`. Their `CacheKey` embeds the **`Tenant:<tenantId>`** scope, so keys are tenant-safe (ADR-013). ✓
- `CacheInvalidationConsumer` (P7) invalidates `policy:effective-subscription:*` and `policy:effective-quota:*` by tenant-scoped glob pattern (`*Tenant:<id>*`) on every config event. These are low-churn + 5m TTL, so tenant-scoped broad invalidation is safe. ✓

### Phase 5 — Infrastructure Audit

| Concern | Implementation | Verdict |
|---------|----------------|---------|
| `PolicyDbContext` | 5 aggregates + `OutboxMessage` + `DeadLetterMessage`; global tenant/soft-delete filter via `CurrentRequestContext`; `SaveChangesAsync` override → outbox capture | ✓ |
| EF mappings | `OwnsMany` for `UsageLedger.Counters`, `DebtLedger.Debts`/`Recoveries`; composite `(TenantId, Id)` keys; `ValueGeneratedNever`; VO `HasConversion`; `Version` concurrency token; `OutboxMessage`→`policy_outbox`, `DeadLetterMessage`→`policy_dead_letter` | ✓ R-1 Option A proven |
| UoW | `PolicyUnitOfWork : ITransactionalUnitOfWork`, registered as `IUnitOfWork` | ✓ |
| RLS | `TenantRlsInterceptor` sets `SET LOCAL app.current_tenant_id`; bypassed when `CanBypassTenantIsolation` | ✓ |
| OPA | `IOpaDataUpdater` (HTTP PUT `/v1/data/{path}`; `null` deletes) + `OpaOptions` bound from `Opa` config | ✓ |
| Recovery | **Lazy First-Access** (no scheduler): `AllowanceEngine.Consume` detects elapsed window boundaries and calls `RecoveryProcessor.Recover` per renewed window inside the same `UnitOfWorkBehavior` transaction; `RecoveryPosition` persisted per (Consumer, Action, Window). The `RecoveryProcessorJob` scheduler is **removed** (ADR-018 §11b). | ✓ |

### Phase 6 — Consumer Audit (P4 / P5 / P7)

All three consumers implement `IConsumer<EventEnvelope>`, use `IEventConsumerDeduplicationGuard.TryBeginProcessingAsync(consumerName, eventId, 7d TTL)`, and filter a tight watch-set before dispatch:

| Consumer | Events | DB touch? | Sets RequestContext? |
|----------|--------|-----------|----------------------|
| `PrincipalHierarchyConsumer` | `authorization.role-created/assigned/revoked.v1` | yes (read model) | ✓ service context, tenant = envelope |
| `OpaSyncConsumer` | `policy.policy-published/archived.v1` | yes (loads Policy) | ✓ service context, tenant = envelope |
| `CacheInvalidationConsumer` | `policy.*` config events | no (Redis only) | n/a |

- **DB-touching consumers set a service `RequestContext`** so the RLS interceptor + global filter resolve to the event's tenant (mirrors how an API request would). Consumer-dispatched internal commands carry `TenantId` from the envelope. ✓
- **`OpaSyncConsumer`** extracts `PolicyId` from the envelope `JsonElement` (`Payload.PolicyId.Value`) because `PolicyId` is a read-only VO that STJ cannot round-trip; it then loads the aggregate and syncs `CompiledRego.Source`. Archive path PUTs `null` (OPA deletes the node). ✓
- **Payload extraction** uses `JsonElement.TryGetProperty` (robust to envelope shape), not brittle deserialization of VOs. ✓

### Phase 7 — ADR Compliance

- **ADR-012** (no `RequestContext` in domain/payloads): ✓ events carry `TenantId`+`CorrelationId` only; consumers set `RequestContext` at the edge, never inside domain.
- **ADR-013** (tenant from accessor, never in command/query): ✓ API commands/queries carry no `TenantId`; only the internal consumer-dispatched command carries it (sourced from the envelope, documented).
- **ADR-015** (single tx owner = `UnitOfWorkBehavior`; handlers pure): ✓ verified in Phase 2.
- **ADR-017** (Outbox-ready): ✓ events implement `IDomainEvent`; `EventTypeName` is routable; outbox captured in `SaveChangesAsync`.
- **ADR-018** (separate aggregates, no quota in Rego, per-consumer lazy recovery): ✓ `RegoModule` excludes thresholds; recovery is triggered lazily by `AllowanceEngine` (no `RecoveryProcessorJob` scheduler); debt dominance handled in `DebtLedger`.

### Phase 8 — Readiness

| Readiness for… | Status | Notes |
|----------------|--------|-------|
| API host (P0) | ⚠ Deferred | `PolicyService.Api` is a placeholder; consumers + `AddPlatformBehaviors` + MassTransit + health checks not yet wired. No code defect — explicit next phase. |
| End-to-end consumption | ✓ Ready | `RecordConsumption` path complete; candidate scopes `[User, Tenant]`. |
| OPA sync | ✓ Ready | consumer + client implemented. |
| Audit (ADR-017) | ✓ Ready | outbox rows emitted; AuditService consumes downstream. |
| Role-aware scopes | ⚠ Enhancement | `PrincipalHierarchyReadModel` exists and is hydrated, but `ResolveEffectiveSubscription/Quota` do not yet consult it to add the `Role` candidate scope. Not in the current phase plan; documented as a future enhancement. |

**Carried findings (non-blocking):**
- F-1 (MEDIUM): `IDebtDominanceSpecification` / `IAllowanceSufficiencySpecification` still unused (inline engine logic is the de-facto rule). `IRecoveryEligibilitySpecification` is now wired. Consider deleting the two unused specs or wiring them.
- F-3 (LOW): a few Domain event payloads still omit catalog-05 fields (carried from Domain audit); non-breaking.
- F-4 (LOW): `SetCompiledRego` raises no domain event (carried).

---

## 2. Risk Assessment

| # | Risk | Severity | Impact |
|---|------|----------|--------|
| R-1 | API host not yet wired (P0) | Medium | Consumers/behaviors don't run until `Program.cs` + MassTransit + `AddPlatformBehaviors` are added. No code defect. |
| R-2 | Role scope not yet expanded into UC-20 candidate scopes | Low | Resolution falls back to User/Tenant; correctness preserved, role precedence not yet applied. |
| R-3 | Two specs unused (F-1) | Low | Maintenance drift risk only. |
| R-4 | Event payload gaps (F-3) | Low | AuditService/OPA receive fewer fields than catalog documents. |

**No Critical or High risks.** No infrastructure leakage into handlers; no handler opens its own transaction; no `RequestContext` inside the domain; tenant isolation enforced centrally.

---

## 3. Build Validation

```
dotnet build src/Services/PolicyService/PolicyService.Infrastructure/PolicyService.Infrastructure.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- All three PolicyService projects (`Domain`, `Application`, `Infrastructure`) build clean (0/0).
- Application references only `SharedKernel`, `Domain`, FluentValidation, MediatR — no direct EF/MassTransit/Redis leakage into handlers.
- Infrastructure owns all EF/MassTransit/OPA/Redis concerns; consumers are the only edge that maps `EventEnvelope` → domain intent.

---

## 4. Readiness Report

- ✅ 13 commands + 11 queries + 1 internal command implement the full UC surface (UC-01..25) with correct marker discipline and pure handlers.
- ✅ Persistence foundation (DbContext, 7 EF configs, 6 repositories, UoW, outbox capture, RLS) builds clean and matches sibling-service conventions.
- ✅ Read-model hydration (P4), OPA sync (P5), and cache invalidation (P7) consumers implemented with dedup guard + tight watch-sets; recovery job (P6) preserves the per-consumer boundary.
- ✅ ADR-012/013/015/017/018 satisfied.
- ⚠ Only non-blocking items remain: API host wiring (R-1, explicit next phase), optional role-scope expansion (R-2), two unused specs (R-3), minor event payload gaps (R-4).

---

## Final Verdict

# READY

The Application + Infrastructure layers are considered **stable and complete for the implemented phases (P1–P7)**. The single explicit next step is **P0 / API host wiring** (placeholder `Program.cs` → full `AddApplication` + `AddInfrastructure` + `AddPlatformBehaviors` + MassTransit consumers + health checks), after which the service is runnable end-to-end. No Application/Infrastructure redesign is required.
