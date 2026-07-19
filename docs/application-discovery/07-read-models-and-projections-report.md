# Policy Service — Read Model Discovery

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`, `01`, `04`, `05`.
- **Authoritative inputs:** ADR-014 (principal hierarchy ownership), ADR-018 §9/§12–13 (local resolution, eventual consistency).

> **Purpose:** enumerate the read-side state PolicyService maintains, what each is for, how it is populated, and how queries consume it. PolicyService is **state + outbox** (not event-sourced); there is exactly **one** materialized read model (the principal hierarchy) plus on-demand computed reads. No separate read database.

---

## 1. Read Models Inventory

| Read model | Kind | Source | Populated by | Consumed by |
|------------|------|--------|--------------|-------------|
| **`PrincipalHierarchyReadModel`** | Materialized projection (table) | AuthorizationService events (UC-26–29) | Inbound consumers → internal commands | UC-13 (sub resolution), UC-19 (quota resolution), UC-20 (scope chain) |
| Effective subscription/quota | **Computed read** (cacheable, `05`) | live `Subscription`/`QuotaPolicy` tables via resolvers | resolved on read | UC-13/19/20 |
| Allowance status | **Computed read** (cacheable 30s) | ledgers + resolvers | resolved on read | UC-23 |
| Raw usage / debt | **Direct aggregate read** | `UsageLedger`/`DebtLedger` (no cache) | `GetByIdAsync(trackChanges:false)` | UC-21/22 |

The only *persistent, separately-maintained* read model is `PrincipalHierarchyReadModel`. Everything else is resolved live from the authoritative aggregates (config) or ledgers (runtime) at read time — which keeps the per-consumer strong-consistency boundary (`02` §3) clean and avoids dual-write read-model sync for high-churn counters.

---

## 2. `PrincipalHierarchyReadModel` (ADR-014)

**Purpose:** give resolution zero runtime coupling to AuthorizationService (`00` §4.6, `04` §3). It stores the User → Role → Tenant membership graph locally.

**Proposed schema (single table + self/junction, or two tables):**

```
principal_nodes (tenant_id, node_id, node_type /* user|role */, version)
principal_edges (tenant_id, user_id, role_id, version)   -- user→role membership
```

- `UserCreated` (UC-26) → insert `user` node + edge to its tenant; also trigger ledger provisioning.
- `RoleAssignedToUser` (UC-27) → upsert `user→role` edge.
- `UserTenantChanged` (UC-28) → move user node (`tenant_id` update) + re-point edges.
- `RoleDefined` (UC-29) → insert `role` node.

**Access patterns:**
- `ISubscriptionResolver.ResolveAsync(tenantId, candidateScopes)` builds `[User, Role(s), Tenant]` from this model, then queries `Subscription`/`QuotaPolicy` by those scopes.
- Maintained **only** by inbound consumers; never mutated by runtime commands.

**Consistency:** eventual. A brief delay between an AuthorizationService membership change and its reflection in PolicyService resolution is acceptable (ADR-014/018 §12–13) — resolution is best-effort against the last known hierarchy.

---

## 3. Computed Reads (resolvers + specs)

- **UC-13 (effective subscription):** `ISubscriptionResolver` queries `Subscription` by scope precedence (User → Role → Tenant) and returns the winning binding. Result cached per `05`.
- **UC-19 (effective quota):** `IQuotaResolver` + `IQuotaResolutionSpecification` pick the applicable `QuotaPolicy` for the scope chain. Cached per `05`.
- **UC-23 (allowance status):** composes effective quota (UC-19) − `UsageLedger.GetCount` − `DebtLedger.OutstandingDebt`. **Never** reads through the cache for the counter parts; only the composite may be cached ≤30s.

These are **not** separate tables — they are queries over the authoritative aggregates wrapped by domain resolvers. No projection maintenance needed.

---

## 4. Ledger Reads (UC-21/22)

- `IUsageLedgerRepository.GetByIdAsync(tenantId, id, trackChanges:false)` → map to DTO.
- `IDebtLedgerRepository.GetByIdAsync(...)` → `OutstandingDebts` / `MonthlyDebt`.
- High-churn, strongly consistent per consumer → **no cache** (`05` §2).

---

## 5. DTO Mapping & Privacy

- Response DTOs (`RecordConsumptionResponse`, `AllowanceStatusDto`, `PolicyDto`, …) are built in handlers/queries from aggregates; they **never** expose domain objects or `DomainEvents`.
- No PII beyond what the hierarchy requires (`ConsumerId`, `ActionKey`, scope) — consistent with ADR-012 (events carry only `TenantId`+`CorrelationId`).

---

## 6. TODO

- [ ] Finalize `PrincipalHierarchyReadModel` schema (single vs two tables; PK/version columns).
- [ ] Implement the internal `UpsertPrincipalEdgeCommand` + repository used by UC-26–29 consumers.
- [ ] Confirm `ISubscriptionResolver`/`IQuotaResolver` scope-precedence ordering matches `00` §4.4 (User→Role→Tenant).
- [ ] Confirm ledger provisioning on UC-26 is idempotent (`GetOrCreateAsync`, `03` §2).

**Next:** `08-implementation-plan-and-todo.md`.
