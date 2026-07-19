# Policy Service — Domain Discovery Report (Part 1: Phases 1–7)

- **Status:** Proposed (discovery, pre-implementation)
- **Date:** 2026-07-15
- **Authoritative inputs:** ADR-001/002/003/011/012/013/014/015/017/018.
- **Convention sources:** `SharedKernel` (AggregateRoot, Entity, ValueObject, IDomainEvent, Error, Result, Guard, Outbox), `Platform.Abstractions` (IUnitOfWork, ITransactionalUnitOfWork, IPlatformOutboxRepository, RequestContext), `AuthorizationService.Domain`, `IdentityService.Domain`.

> The Domain Model is derived **from business rules + ADRs only**. No prototype assumptions. Where a prototype conflicts, the business model / ADR wins.

## Phase 0 — Constraints distilled from ADRs

| ADR | Binding constraint for PolicyService |
|-----|--------------------------------------|
| 011 | Owns ABAC, Quotas, Usage, Debt, Subscription hierarchy, OPA gen/compile/distribute/evaluate. Does **not** model org structure. References `TenantId`/`DepartmentId`/`RoleId`/`UserId` as immutable external identity references (SubjectId pattern). |
| 012 | `RequestContext` is Platform-owned, read-only infra state. **Never** embedded in Commands, Domain Events, or Aggregate state. Domain events carry `TenantId` + `CorrelationId` only. |
| 013 | `TenantId` propagated post-auth; handlers read from accessor; domain never resolves. All commands tenant-scoped. |
| 014 | Department membership derived from `Role` (AuthorizationService). User has **no** `DepartmentId`. Store `DepartmentId` as external reference only; never resolve at runtime. |
| 015 | `UnitOfWorkBehavior` is sole transaction owner; handlers pure; `DbContext` captures `DomainEvents` → `OutboxMessage` in `SaveChangesAsync`. |
| 017 | Outbox + Audit integration mandatory; audit-relevant events published to AuditService. |
| 003 | OPA is external engine; PolicyService owns Rego generation/compile/distribution/evaluation. AuthorizationService never contains OPA logic. |
| 018 | Quota definition, usage, debt, recovery, reset are **distinct** concepts; not one `Quota` aggregate. |

**Separation of concerns (business rule):** AuthorizationService answers *"Can user U perform action A?"* (RBAC). PolicyService answers *"Given RBAC allowed A, under what conditions is it still allowed?"* (ABAC + quota + debt). These are different responsibilities.

## Phase 1 — Business Concepts (catalog)

| # | Concept | Kind | Exists because | Notes |
|---|---------|------|----------------|-------|
| 1 | **Policy** (ABAC definition) | Aggregate | Reusable authorization rule (conditions + Rego) | "what" is allowed/denied under conditions |
| 2 | **Subscription** | Aggregate | Binds a Policy to a scope (Tenant/Role/User) with precedence | "where it applies"; resolved User→Role→Tenant |
| 3 | **QuotaPolicy** | Aggregate | Per-scope allowance configuration (Daily/Weekly/Monthly) | Definition, hierarchical |
| 4 | **Quota** | Value Object | The per-window limits inside QuotaPolicy | No identity/lifecycle of its own |
| 5 | **UsageLedger** | Aggregate | Per-consumer, per-action consumption tallies | Operational state; independent counters per action |
| 6 | **UsageCounter** | Value Object | Count + window range for one window | Inside UsageLedger |
| 7 | **DebtLedger** | Aggregate | Outstanding debt + recovery per consumer | Separate liability; survives rollover |
| 8 | **Debt** | Value Object | Outstanding debt amount (non-negative) | Inside DebtLedger |
| 9 | **RecoveryPosition** | Value Object | Remaining allowance after debt deduction | Inside DebtLedger |
| 10 | **ActionKey** | Value Object | e.g. `rss.read`, `invoice.export` | Usage/debt keyed by action |
| 11 | **ResourceType / ResourceId** | Value Object | Scope of an action | Targeting |
| 12 | **SubscriptionScope** | Value Object | Tenant/Role/User + principal id | Resolution key |
| 13 | **ConsumerId** | Value Object | References User (IdentityService) | External identity ref |
| 14 | **TenantId / RoleId / DepartmentId / UserId** | Value Object | External identity references | SubjectId pattern |
| 15 | **QuotaWindow** | Value Object (enum) | Daily / Weekly / Monthly | Linked windows |
| 16 | **PolicyCondition / PolicyExpression** | Value Object | ABAC condition tree | Immutable |
| 17 | **PolicyPriority** | Value Object | Evaluation order | |
| 18 | **RegoModule** | Value Object | Immutable compiled Rego AST | OPA-independent |
| 19 | **Consumption Operation** | Domain Event | A consumer consumed N units | Trigger for accounting |
| 20 | **Reset** | Command (not state) | Admin clears usage+debt | Does not alter QuotaPolicy |

## Phase 2 — Aggregate Boundaries (Vernon)

Proposed aggregates: **Policy, Subscription, QuotaPolicy, UsageLedger, DebtLedger** (+ optional PolicyVersion as child of Policy).

- Each aggregate references others **by id** (ConsumerId, PolicyId, SubscriptionId), never by object.
- Consistency boundary = one consumer's runtime accounting (UsageLedger + DebtLedger updated together) and one configuration object (QuotaPolicy / Subscription / Policy).
- Cross-aggregate invariants (e.g. "operation succeeds + debt recorded", "any outstanding debt blocks all windows") are coordinated by **domain services**, not by nesting.
- Transaction boundary: a single consumption command loads UsageLedger + DebtLedger + resolved QuotaPolicy and commits in one `UnitOfWorkBehavior` transaction.

## Phase 3 — Ownership decisions (justification)

| Concept | Owned by | Justification |
|---------|----------|---------------|
| Policy / Subscription / QuotaPolicy / UsageLedger / DebtLedger | **PolicyService** | Business assigns all to PolicyService (ADR-011). |
| Tenant / Department | TenantService | PolicyService stores `TenantId`/`DepartmentId` as external refs only. |
| User / Consumer | IdentityService | `ConsumerId` is external ref. |
| Role / Permission / Role Assignment | AuthorizationService | `RoleId` external ref; RBAC is answered there. |
| Department membership | derived from Role (AuthorizationService) | PolicyService never resolves; stores `DepartmentId` ref if scoped. |
| Audit history | AuditService | PolicyService emits events; never stores audit log. |

PolicyService must **not** call TenantService/AuthorizationService/IdentityService at runtime (ADR-011/013/014). A local **principal-hierarchy read model** is hydrated asynchronously from their events for Subscription/QuotaPolicy resolution.

## Phase 4 — Usage Tracking: separate aggregate (YES)

**Decision: Usage Tracking is a SEPARATE aggregate (`UsageLedger`), never inside Policy/Subscription/QuotaPolicy.**

Justification:
1. **Different lifecycle** — usage mutates on every operation and **resets per window**; policy/quota config changes rarely. Vernon: different lifecycles ⇒ different aggregates.
2. **Different consistency boundary** — consumption is keyed by `(ConsumerId, ActionKey)` with **independent per-action counters**; it has no semantic link to a specific Policy definition.
3. **Contention** — embedding usage in Policy/QuotaPolicy would force every feed read to load/lock the configuration aggregate.
4. **Volatility vs stability** — config is stable; usage is high-churn. Keeping them apart honours "keep aggregates small".
5. **Per-action independence** — the business requires independent counters per action (`rss.read` ≠ `invoice.export`); this is operational state, not policy.

## Phase 5 — Subscription / Quota / Debt classification

**Subscription → AGGREGATE.** It has an independent lifecycle (define → activate → revoke → supersede), references `PolicyId`, is scoped via `SubscriptionScope`, and is the unit of hierarchical resolution. It is not a VO because it has identity + lifecycle + repository + events.

**Quota → VALUE OBJECT.** The per-window limits (`Daily/Weekly/Monthly`) have no identity or lifecycle apart from their owning `QuotaPolicy`. They are immutable configuration data → VO inside `QuotaPolicy`.

**Debt → AGGREGATE (`DebtLedger`).** Reasons:
1. **Different lifecycle from usage** — usage resets each window; debt **survives rollover** and is reduced only by recovery. Vernon: different lifecycle ⇒ separate aggregate.
2. **Separate business liability** — debt is explicitly *not* negative quota; it must be independently queryable, auditable, and recoverable.
3. **Recovery coupling** — recovery (renewed allowance − debt) mutates debt, not usage; colocating debt in UsageLedger would couple reset-lifecycle with survival-lifecycle.
4. **Independently referenced** — the monthly-dominance rule and reset both need debt without touching usage tallies.

## Phase 6 — Lifecycle of each aggregate

| Aggregate | Creation | Activation | Modification | Expiration | Reset | Deletion |
|-----------|----------|-----------|--------------|-----------|-------|----------|
| Policy | `Policy.Create` (Draft) | Validate→Compile→Publish→Active | Add/remove conditions, recompile | Archive (disabled) | n/a | Soft-delete (IsDeleted) |
| Subscription | `Subscription.Assign` | Activate (effective) | Change scope/precedence | Revoke/Supersede | n/a | Soft-delete |
| QuotaPolicy | `QuotaPolicy.Define` | (effective immediately) | Amend limits/scope | n/a | n/a | Soft-delete |
| UsageLedger | created lazily on first consumption | open | incremented per op; window reset on rollover | n/a | **Reset** (admin) | Soft-delete (consumer offboard) |
| DebtLedger | created lazily on first debt | open | debt incurred / recovered | n/a | **Reset** (admin) | Soft-delete (consumer offboard) |

## Phase 7 — Invariants (summary; full list in `04-invariant-report.md`)

1. Allowance never negative; `Quota` limits non-negative.
2. Debt never disappears; debt cannot be negative (it is a non-negative outstanding amount).
3. Consumption **always succeeds**; results never truncated to fit quota.
4. Excess consumption ⇒ debt (single liability per (Consumer, Action); **not** per window).
5. Debt survives period rollover; recovery deducts debt first: `Available = RenewedAllowance − OutstandingDebt`; any window's renewal repays the single debt (lazy first-access, ADR-018 §11b).
6. No access until debt reaches zero.
7. Any outstanding debt (single per action) **dominates** all windows (Daily/Weekly/Monthly blocked while `OutstandingDebt(action) > 0`).
8. Hierarchy precedence deterministic: User → Role → Tenant (first defined wins).
9. Reset clears usage+debt+counters+derived state **without corrupting history** (audit history in AuditService untouched).
10. `TenantId` is always present and equals the requesting tenant (ADR-013); never resolved in domain.
11. Domain events carry `TenantId`+`CorrelationId` only; never `RequestContext` (ADR-012).
