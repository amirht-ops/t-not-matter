# ADR-018: Policy / Quota / Debt / Recovery Domain

- **Status:** Approved
- **Date:** 2026-07-15
- **Approved:** 2026-07-16 (architecture decisions: debt-per-action scope, persisted RecoveryPosition, Lazy Recovery, scheduler removed)
- **Deciders:** Domain-Driven Design / Enterprise Architecture
- **Tags:** policy-service, quota, debt, recovery, bounded-context, aggregates

---

## 0. Context

The organization operates an internal platform that **provides internal data feeds**
(RSS, web feeds, crawled public information, other internal feed providers). Users
consume these feeds. The organization grants each user an **internal allowance** of feed
consumption — this is **not** a purchased subscription and **not** a rate-limit product; it
is an organizational capacity entitlement.

Prior direction started from an implementation (a `PolicyService`, OPA/Rego generation,
Redis counters, a single `Quota` aggregate). This ADR deliberately **ignores that
implementation**. Its single purpose is to discover and formalize the **business domain
first**, prove the aggregate boundaries from the business rules, and establish the canonical
model that any future implementation must obey. Where an existing implementation contradicts
this model, **the business model wins**.

The business has supplied explicit, non-negotiable rules about:

- three linked allowance windows (Daily / Weekly / Monthly),
- non-incremental consumption (one operation may consume hundreds of units),
- **business debt** when an operation exceeds remaining allowance,
- **recovery** of debt through future allowance on window rollover,
- **monthly debt dominance** over lower windows,
- administrative **reset** of operational state,
- a **hierarchical** quota configuration (Tenant → Role → User).

These rules are analyzed below as the source of truth.

---

## 1. Business Motivation

- The organization wishes to **cap internal feed consumption** per user as an
  organizational governance control, not as a commercial metering product.
- Consumption must remain **user-friendly**: an operation that returns 460 results must
  return all 460; the system must **never truncate** results to stay within quota.
- Over-consumption is tolerated operationally but is recorded as a **business liability
  (debt)** that the user must repay from future allowance.
- The business wants a predictable, forgiving recovery mechanism rather than hard
  denial, while still enforcing ultimate limits through the monthly window.
- Operations staff must be able to **intervene** (reset) without altering the standing
  business configuration.

---

## 2. Ubiquitous Language

| Term | Business meaning |
|------|------------------|
| **Feed** | An internal data source (RSS, web, crawl, internal provider) whose items are consumed. |
| **Feed Consumption** | A user operation that reads feed items; measured in **units** (items consumed). |
| **Allowance** | The internal organizational capacity granted to a consumer for a window. Never negative. |
| **Window** | One of three linked periods: **Daily**, **Weekly**, **Monthly**. |
| **Quota Policy** (a.k.a. Quota Definition / Allowance Policy) | The **configuration** of per-window limits, owned at some level of the org hierarchy. |
| **Usage** (Quota Usage) | The **operational tally** of units consumed within a window in the current period. |
| **Debt** | The **excess** consumption not covered by remaining allowance. A separate business liability. Survives rollover. Never negative quota. |
| **Recovery** | The automatic process of deducting outstanding debt from renewed allowance at window start. |
| **Reset** | An administrative operation that clears usage, debt and recovery state. **Not** a change to quota policy. |
| **Debt Dominance** | When **any** outstanding debt exists (a single liability per action, denominated in allowance units), all windows are effectively blocked until it is repaid. Debt is NOT attached to a window (authoritative business rule). |
| **Resolution Order** | User → Role → Tenant; the first defined quota policy wins (configuration inheritance). |
| **Consumer** | The entity that consumes feeds: a **User** (identified by id). |
| **Principal** | A Tenant, Role, or User that a quota policy may be defined against. |

The language deliberately never says "negative quota", "rate limit", "counter", or "cache".

---

## 3. Business Concepts

### 3.1 Feed and Consumption
- A **Feed** is provided by a separate feed-provisioning capability; PolicyService does not
  own feed content.
- A **Consumption Operation** is a business event: *a consumer performed an operation that
  consumed N units*. The operation **always succeeds** and returns its full result set.

### 3.2 Allowance Windows
Three windows exist: **Daily (e.g. 20)**, **Weekly (e.g. 100)**, **Monthly (e.g. 300)**.
They are **linked**, not independent: the Monthly window dominates the lower two.

### 3.3 Quota Policy (Definition)
The per-window limits. Defined **hierarchically**:
- **Tenant** provides the default policy.
- **Role** may override Tenant.
- **User** may override Role.
- Resolution is **User → Role → Tenant**, first available wins.

This is **configuration**, changed rarely, owned by the defining principal.

### 3.4 Usage (Operational Tally)
For each consumer and each window, the count of units consumed in the **current** period.
Reset to zero when that window rolls over.

### 3.5 Debt (Business Liability)
If an operation consumes more than the remaining allowance, the excess is recorded as
**Debt**. Debt:
- is **not** negative quota,
- is a **separate ledger** from usage,
- **survives period rollover**,
- is reduced only by recovery.

### 3.6 Recovery
When **any** window (Daily, Weekly, or Monthly) begins anew, the allowance is
renewed **but existing outstanding debt is deducted first**:
`Available = RenewedAllowance − OutstandingDebt`.
The consumer keeps consuming (and repaying) until debt reaches zero, after which full
allowance is restored. Recovery is **automatic**, driven by future allowance, and is
triggered **lazily on first access after the renewal boundary** (see §5 / §11 — no scheduler).

> **Approved decision — Debt scope.** Outstanding debt is maintained **per (Consumer, Action)**.
> Each action owns an independent debt position. Renewals for that action repay only that
> action's outstanding debt. Debt is **never** stored per quota window.

> **Approved decision — `RecoveryPosition` is persisted domain state.** `RecoveryPosition`
> (the remaining usable allowance for a specific window after debt-first recovery) is a
> **persisted part of the aggregate's business state**, held per **(Consumer, Action, Window)**,
> not a transient calculation. It is written whenever recovery is applied and read during
> allowance evaluation.

### 3.7 Debt Dominance
If any outstanding debt exists (a single liability per action), Daily and Weekly access are
**effectively blocked**. A consumer cannot consume usable allowance while debt remains; the
next renewal repays debt first, then releases allowance.

### 3.8 Administrative Reset
An administrator may reset a consumer's allowance. The reset clears **usage, debt, and
recovery state** but **does not change the assigned quota policy**. Reset is operational
intervention, distinct from configuration change.

---

## 4. Ownership Model

| Concept | Business owner | Notes |
|---------|----------------|-------|
| Tenant / Role / User identity & membership | **AuthorizationService** (external BC) | PolicyService references these only by id. |
| Feed definitions / content | **Feed Provider** (external BC) | PolicyService reacts to consumption events. |
| Quota Policy (definition) | **PolicyService** | Scoped to a principal (Tenant/Role/User id). |
| Usage Ledger | **PolicyService** | Keyed by consumer (User id). |
| Debt Ledger (+ Recovery) | **PolicyService** | Keyed by consumer (User id). |
| Consumption Events | **PolicyService** (emitted) / Feed Provider (raised) | The trigger for allowance accounting. |
| Audit Records | **AuditService** (external BC, consumer of events) | Debt/recovery/reset are audit-relevant. |

**Key point:** PolicyService **does not own** users, roles, or tenants. It owns the
*allowance configuration and the runtime allowance state* expressed **in terms of** those
external principal ids.

---

## 5. Aggregate Discovery

We discover aggregates from **invariants and lifecycles**, not from a "Quota" noun.

### Candidate concepts and their lifecycles

| Concept | Lifecycle | Mutation frequency | Couples with |
|---------|-----------|--------------------|--------------|
| Quota Policy | defined → amended (rare) | low | hierarchy (Tenant/Role/User) |
| Usage (per window) | increments per op; **resets each period** | very high | debt (on exceed), dominance rule |
| Debt (single, per action) | NOT window-scoped; survives rollover; repaid by ANY renewal | low | repaid debt-first by any window renewal |
| Debt | incurred on exceed; **survives rollover**; reduced by recovery | medium | recovery, dominance |
| Recovery | **recomputed at rollover**; advances as debt repaid | low/medium | debt, renewed allowance |
| Reset | administrative, on demand | rare | clears usage + debt + recovery |
| Consumption Operation | instantaneous event | very high | usage + debt |

### Observations (Vernon's rules)
1. **Different lifecycles ⇒ different aggregates.** Policy changes rarely; usage churns
   constantly and resets; debt survives rollover; recovery recomputes on rollover. They
   cannot share one aggregate without violating the "same lifecycle" test.
2. **True invariants define consistency boundaries.** The dominance rule and the
   "operation succeeds + debt recorded" rule require, at consumption time, a coordinated
   view of *all three windows* **and** the *current debt*. That is one consistency boundary
   for the **runtime state** of a consumer.
3. **Reference other aggregates by identity.** The runtime state references the resolved
   Quota Policy by id; it never embeds the policy.

---

## 6. Aggregate Validation — "Is one `Quota` aggregate sufficient?"

**Assumption challenged: NO. A single `Quota` aggregate is insufficient.** Proof:

1. **Mixed lifecycles.** A monolithic `Quota` would hold policy (rarely changed), usage
   (changes every operation, resets per period), debt (survives rollover), and recovery
   (recomputed on rollover) in one object. Vernon requires same-lifecycle co-location; this
   fails on every axis.
2. **Hierarchical ownership cannot be expressed.** The policy is defined at Tenant, Role,
   **or** User, and is *resolved* by walking the hierarchy. A consumer-owned `Quota`
   aggregate cannot represent an inherited, higher-level definition; the resolution is a
   cross-owner read, not internal state.
3. **Conceptual conflation violates invariants.** Packing debt into the same object invites
   modelling it as a negative number ("negative quota") and recovery as a reset — both
   explicitly forbidden by the business.
4. **Reset hazard.** Admin reset must clear operational state **without** touching policy.
   If policy and usage share one aggregate, a reset command risks altering configuration.
   Separation makes "clear usage+debt+recovery, leave policy" trivially safe.
5. **Transaction contention.** Every consumption would load the full policy+debt+recovery
   graph. Policy is configuration; coupling it to the hot consumption path is unnecessary
   contention.
6. **Dominance rule needs cross-window + debt visibility.** A per-window `Quota` cannot
   express "monthly debt blocks daily/weekly".

**Conclusion:** the canonical model requires **at least two aggregates** — a configuration
aggregate and a runtime-state aggregate — and treats usage, debt, and recovery as **distinct
model elements** even where they share a runtime consistency boundary.

---

## 7. Recommended Aggregate Model

### Aggregate A — `QuotaPolicy` (Definition / Configuration)
- **Identity:** scoped to a principal — `(Scope: Tenant|Role|User, PrincipalId)`.
- **State (Value Objects):** `AllowanceLimit` per window (Daily/Weekly/Monthly); optional
  enabled flag; version.
- **Lifecycle:** `Defined → Amended`. Resolved, not mutated, at consumption time.
- **Invariants:** limits are non-negative; a policy applies to exactly one scope.
- **Ownership:** the defining principal (Tenant/Role/User). Multiple policies may exist at
  different levels; the **effective** policy is produced by `QuotaResolutionSpecification`.
- **Repository:** `IQuotaPolicyRepository` (query by scope; list by principal).

### Aggregate B — `UsageLedger` (Runtime Consumption Tally)
- **Identity:** `ConsumerId` (User id). Holds **all three windows** so the dominance rule
  can be enforced atomically.
- **State:** `WindowUsage` value object containing `Daily`, `Weekly`, `Monthly` tallies
  (units consumed in the current period) + window period markers.
- **Lifecycle:** perpetually open; tallies reset on each window's rollover.
- **Invariants:** usage never negative; all windows are blocked while **outstanding debt** (single per action) is > 0 (inv 7).
- **Repository:** `IUsageLedgerRepository` (by consumer).

### Aggregate C — `DebtLedger` (Business Liability + Recovery)
- **Identity:** `ConsumerId` (User id).
- **State:** `DebtPosition` (outstanding debt, never negative) **and** `RecoveryPosition`
  (remaining allowance after debt deduction at last rollover, kept **per (action, window)**). These are **separate,
  named concepts** — debt is not negative usage; recovery is not debt. **Debt is a single liability per (consumer, action); it has NO window axis.** The window dimension lives only in `UsageLedger` (tally) and in `RecoveryPosition` (per-window post-repayment usable allowance).
- **Lifecycle:** `Incurred → (Recovered in part) → Repaid → Dormant`; survives rollover.
- **Invariants:** debt ≥ 0; recovery may only reduce debt; recovery uses *renewed* allowance
  minus outstanding debt.
- **Repository:** `IDebtLedgerRepository` (by consumer).

> **Design note on co-location vs. strict split.** Usage, Debt and Recovery are distinct
> business concepts (per the explicit instruction not to merge them). They are therefore
> modelled as **distinct aggregates / repositories** (`UsageLedger`, `DebtLedger`) with
> `QuotaPolicy` separate. The cross-aggregate invariants (operation succeeds + debt
> recorded; monthly dominance) are coordinated by a **domain service / process manager**
> (`AllowanceEngine`, see §11) that loads the relevant ledgers together and persists them in
> a single unit of work **within PolicyService** — preserving strong consistency for one
> consumer while keeping the aggregates conceptually and transactionally clean. (A stricter
> event-only saga variant is permissible if cross-service persistence later splits these
> stores; the consistency trade-off is documented in §9.)

### Command, not Aggregate — `Reset`
Reset is an **administrative command** executed by the `AllowanceEngine` (or an
`AllowanceAdministrationService`) against `UsageLedger` + `DebtLedger`. It is **not** an
aggregate and **not** state. It clears usage, debt and recovery but never touches
`QuotaPolicy`.

---

## 8. Invariants (canonical list)

1. **Allowance is non-negative.** No window allowance is ever negative.
2. **Debt is separate and non-negative.** Debt is never modelled as negative quota.
3. **Consumption always succeeds.** An operation returns its full result set regardless of
   remaining allowance; results are **never truncated** to fit quota.
4. **Excess becomes debt.** When an operation exceeds remaining allowance, the excess is
   recorded as debt in the same logical operation.
5. **Single debt per action, survives rollover.** Debt is a SINGLE outstanding liability per
   (consumer, action), denominated in allowance units. It is NOT attached to a quota window.
   Period boundaries do not erase debt.
6. **Recovery deducts debt first.** On **any** window renewal: `Available = RenewedAllowance −
   OutstandingDebt`. Full allowance for that window returns only after debt reaches zero.
7. **Any outstanding debt dominates.** If `OutstandingDebt(action) > 0`, Daily/Weekly/Monthly consumption are all effectively blocked. Debt is a single liability per (consumer, action); it is NOT window-scoped (authoritative business rule).
8. **Resolution order is fixed.** Effective quota = first defined among User → Role →
   Tenant.
9. **Reset ≠ reconfiguration.** Admin reset clears usage/debt/recovery; it does not alter
   the assigned quota policy.
10. **Distinct concepts.** Quota definition ≠ usage ≠ debt ≠ recovery ≠ reset. Each has its
    own aggregate/service.

---

## 9. Transaction Boundaries & Consistency Model

- **Within a consumer:** `UsageLedger` and `DebtLedger` are updated together by the
  `AllowanceEngine` in a **single transaction** (same persistence store, same BC). This gives
  strong consistency for the "succeed + record debt" and "dominance" invariants **per
  consumer**.
- **`QuotaPolicy` is eventually consistent w.r.t. runtime:** it is resolved by id at
  consumption time. Policy amendments are rare and safe to propagate via events; a consumer
  operation does not require a lock on the policy aggregate.
- **Across services:** all cross-BC interaction is **event-driven** (eventual consistency).
  PolicyService emits domain events; AuthorizationService / AuditService / Feed Provider
  consume them. PolicyService never blocks on another service at consumption time.
- **Concurrency:** contention is per-consumer only; there is no cross-consumer debt, so
  per-consumer optimistic concurrency is sufficient.

---

## 10. Domain Events

| Event | Producer | Consumer(s) | Meaning |
|-------|----------|-------------|---------|
| `QuotaPolicyDefined` | QuotaPolicy | local read model | A policy was created at a scope. |
| `QuotaPolicyAmended` | QuotaPolicy | local read model | Limits changed (config only). |
| `FeedConsumed` | Feed Provider (or PolicyService edge) | AllowanceEngine | A consumer consumed N units. |
| `UsageRecorded` | UsageLedger | AuditService | Tally updated for a window. |
| `AllowanceExhausted` | AllowanceEngine | AuditService, notifications | Remaining allowance hit zero. |
| `DebtIncurred` | DebtLedger | AuditService, Analytics | Excess recorded as a single (action) liability (no window axis). |
| `DebtRecovered` | DebtLedger | AuditService, Analytics | Debt reduced by a window's renewal; carries `Window`, `AmountRecovered`, `RemainingDebt`. |
| `WindowRolledOver` (Monthly/Weekly/Daily) | Lazy first-access (`AllowanceEngine`) — **no scheduler** | AllowanceEngine | Period boundary; recovery is detected and applied during the next request processing, not by a background job. |
| `RecoveryApplied` | DebtLedger | AuditService | Debt reduced by renewed allowance. |
| `AllowanceReset` | AllowanceAdministrationService | AuditService | Admin cleared operational state (policy untouched). |

Note: there is **no** "ConsumptionRejected" event — the business mandates success.

---

## 11. Domain Services, VOs, Factories, Specifications, Repositories

**Domain Services / Process Managers**
- `QuotaResolutionSpecification` / `QuotaResolver` — walks User→Role→Tenant, returns the
  effective `QuotaPolicy` (first defined wins). Needs the principal hierarchy (see §12).
- `AllowanceEngine` — coordinates a consumption: resolve policy, load ledgers, record usage,
  incur debt if exceeded, enforce dominance, persist atomically.
- `RecoveryProcessor` — on **each** `WindowRolledOver` (Daily/Weekly/Monthly), **driven lazily by
  `AllowanceEngine` during request processing (first access after the renewal boundary), NOT by a
  scheduler**, repays the single outstanding debt (per action) by that window's renewed allowance
  and **persists** the per-window `RecoveryPosition` (remaining usable allowance for that window).
  Debt is window-agnostic; only the renewed allowance and the persisted `RecoveryPosition` are
  window-specific. `RecoveryPosition` is durable aggregate state, not a transient computation.
- `AllowanceAdministrationService` — executes `Reset` (clears UsageLedger + DebtLedger only).

**Value Objects**
- `WindowKind` (Daily | Weekly | Monthly)
- `AllowanceLimit` (per-window non-negative int)
- `ConsumedUnits` (positive int)
- `DebtAmount` (non-negative)
- `RecoveryAmount` (non-negative)
- `PrincipalScope` (kind + id)
- `WindowUsage` (daily/weekly/monthly tallies + period markers)
- `DebtPosition`, `RecoveryPosition`
- `FeedReference`, `OperationReference`

**Factories**
- `QuotaPolicyFactory` — create policy at Tenant/Role/User scope.
- `UsageLedgerFactory`, `DebtLedgerFactory` — create per-consumer ledgers (idempotent).

**Specifications**
- `QuotaResolutionSpecification` (hierarchy first-wins)
- `DebtDominanceSpecification` (any outstanding debt blocks all windows)
- `AllowanceSufficiencySpecification` (remaining after debt ≥ requested?)
- `RecoveryEligibilitySpecification` (debt > 0 at rollover)

**Repository ownership (within PolicyService)**
- `IQuotaPolicyRepository` → `QuotaPolicy`
- `IUsageLedgerRepository` → `UsageLedger`
- `IDebtLedgerRepository` → `DebtLedger`

---

## 11b. Recovery Trigger — Approved Mechanism (Lazy First-Access)

> **Approved decision — Lazy Recovery is the canonical mechanism; the scheduler is removed.**
> The scheduled `RecoveryProcessorJob` is **removed** from the reference architecture. Recovery is
> triggered by the **first request that arrives after a Daily, Weekly, or Monthly renewal boundary**.
> On that first access, `AllowanceEngine` must:
> 1. **Detect** every elapsed renewal event (each window whose boundary fell since the last recovery
>    marker, per `RecoveryPosition.WindowEnd`).
> 2. **Apply debt-first recovery** for each renewed window (`RecoveryProcessor.Recover`), reducing the
>    single `OutstandingDebt(action)` and persisting the updated `RecoveryPosition` for that window.
> 3. **Persist** the updated `OutstandingDebt` and `RecoveryPosition` inside the same
>    `UnitOfWorkBehavior` transaction as the consumption (ADR-015).
> 4. **Continue** normal quota evaluation.
>
> The scheduler-based design (periodic full-scan, watermark management, timing concerns, operational
> overhead) is retained here **only as a historical / alternative approach** — it is **not** the
> canonical design and MUST NOT be re-introduced without a new ADR.

---

## 12. Cross-service Ownership

- **AuthorizationService** owns Tenant, Role, User and their memberships. PolicyService
  references them **by id only** and must resolve the quota hierarchy. It must **not** call
  AuthorizationService at consumption time. Instead, PolicyService maintains a **local read
  model** of the principal hierarchy, hydrated by events (`UserCreated`,
  `RoleAssignedToUser`, `UserTenantChanged`, `RoleDefined`, …). `QuotaResolver` queries this
  local model.
- **Feed Provider** owns feeds. It raises `FeedConsumed` (or PolicyService's edge does after
  a successful operation). PolicyService reacts; it never manages feed content.
- **AuditService** owns immutable audit logs. It consumes the events in §10; debt, recovery
  and reset are first-class audit subjects because they are financially/operationally
  meaningful business states.
- **PolicyService** owns: `QuotaPolicy`, `UsageLedger`, `DebtLedger`, the resolution read
  model, and all domain events.

---

## 13. Future Integration with AuthorizationService

- Establish a **principal-hierarchy read model** in PolicyService, eventually consistent via
  AuthorizationService domain events.
- `QuotaPolicy` scopes reference AuthorizationService principal ids; no ownership claimed.
- When a user is created/moved, PolicyService provisions (or lazily creates) the consumer's
  `UsageLedger` + `DebtLedger` keyed by the user id.
- Quota resolution (User→Role→Tenant) runs entirely against the local read model — zero
  runtime coupling to AuthorizationService availability.

## 14. Future Integration with AuditService

- Every event in §10 is published to AuditService.
- Special emphasis: `DebtIncurred`, `RecoveryApplied`, `AllowanceReset` are **mandatory
  audit events** (liability creation, repayment, and operational intervention must be
  traceable).
- AuditService is a pure consumer; it never mutates PolicyService state.

## 15. Future Integration with OPA

- **OPA is a downstream enforcement/query surface, never the source of truth for allowance
  state.** Debt, recovery and rollover are **stateful, history-dependent business rules**
  that cannot be expressed as stateless Rego.
- PolicyService remains the authoritative owner of `UsageLedger` and `DebtLedger`.
- Integration options (choose during implementation):
  1. PolicyService **publishes** computed effective-allowance facts; OPA consults them for
     edge-level decisions, or
  2. OPA **defers** quota decisions to a PolicyService query (external data) at decision time.
- Any prior approach that **generated Rego to encode quota/debt/recovery logic** is rejected
  by this ADR: it conflates business state with policy text and breaks the debt/recovery
  invariants.

---

## 16. Migration Strategy

1. **Freeze** the current single-`Quota`-aggregate assumption; treat this ADR as canonical.
2. **Extract `QuotaPolicy`** from runtime state; rebuild resolution (User→Role→Tenant) via a
   local hierarchy read model sourced from AuthorizationService events.
3. **Introduce `UsageLedger` and `DebtLedger`**; backfill `DebtLedger` from any existing
   negative/over-limit counters (treat over-consumption as incurred debt).
4. **Implement `RecoveryProcessor`** as debt-first recovery on a renewal event, **invoked lazily by
   `AllowanceEngine` on first access after a window boundary** (no scheduler, no `RecoveryProcessorJob`).
5. **Implement `AllowanceAdministrationService.Reset`** that clears usage+debt+recovery only;
   verify it never mutates `QuotaPolicy`.
6. **Wire audit events** to AuditService (debt/recovery/reset mandatory).
7. **Retire** any Rego/OPA-based quota enforcement; PolicyService becomes authoritative.
8. Validate behaviour against the invariant list (§8) with executable specs before cutover.

---

## 17. Open Questions

1. **Window anchoring:** calendar-aligned (e.g. calendar month) vs. rolling (trailing 30
   days)? Business phrasing ("when the next monthly window begins") implies calendar; confirm.
2. **Role-level semantics:** is a Role quota a **shared pool** across all role members, or
   does **each member** receive the Role's limit individually? This changes ledger keying
   (per-user vs per-role-pool) and the resolution semantics.
3. **Tenant-level semantics:** default applied per-user, or a shared org pool? Same question
   as above at the tenant level.
4. **Debt ceiling:** the business does not cap debt. Confirm unbounded debt accrual is
   acceptable (operational risk), or define a max-debt / suspension policy.
5. **Denomination:** confirm debt and recovery are denominated in the same **units** as
   allowance (feed items).
6. **Rollover trigger:** **decided — lazy first-access.** Recovery is triggered lazily by
   `AllowanceEngine` before each consumption when a window boundary has elapsed (no scheduler,
   no `RecoveryProcessorJob`). This naturally realizes "per renewal event" recovery inside the
   same transaction as consumption (ADR-015).
7. **Scope of allowance:** does the allowance apply **aggregately across all feeds**, or per
   feed type? Business implies aggregate consumption; confirm.
8. **Reset scope:** confirm admin reset is strictly per-user; clarify whether tenant/role
   *usage* (if pooled) can also be reset without altering policy.
9. **Real-time rate limiting:** prior artefacts mention per-second rate limits. Is that a
   fourth window/concept, or out of scope for this business model?
10. **Concurrent consumption** at very high throughput: confirm per-consumer strong
    consistency (single transaction) is sufficient and no cross-consumer debt exists.

---

## 18. Decision

Adopt the canonical business domain above. Specifically:

- Model **`QuotaPolicy`** (definition, hierarchical, eventually consistent) as a **separate
  aggregate** from runtime state.
- Model runtime allowance state as **distinct aggregates** `UsageLedger` and `DebtLedger`
  (with `RecoveryPosition` as a **persisted** separate concept inside `DebtLedger`, per
  (Consumer, Action, Window)), coordinated per consumer by the `AllowanceEngine` in a single
  transaction.
- Treat **Reset** as an administrative **command**, not state.
- Enforce all invariants in §8; emit the events in §10; integrate with AuthorizationService
  (read model), AuditService (events), and OPA (downstream only) per §12–§15.
- Any implementation that predates this ADR (single `Quota` aggregate, Rego-encoded quota,
  Redis counters) is superseded by the business model.

## 19. Consequences

**Positive**
- Clear separation of configuration vs. operational state vs. liability vs. recovery.
- Aggregates align with Vernon's rules (lifecycle, consistency boundary, identity
  references); no god-aggregate.
- Debt and recovery are explicit, auditable business concepts.
- Consumption path is decoupled from AuthorizationService and OPA at runtime.
- Admin reset is safe by construction (policy untouched).

**Negative / Trade-offs**
- More aggregates and a coordinator (`AllowanceEngine`) than a single `Quota` object.
- Requires a local principal-hierarchy read model (eventual consistency) for resolution.
- If the stores for the ledgers are later split across services, the per-consumer single
  transaction becomes a saga with brief eventual-consistency windows (documented; acceptable
  because debt accrual is tolerant of short delay).
- Slightly more upfront modelling effort, paid back by correctness of debt/recovery.

---

## 20. Policy Execution Pipeline

The canonical **runtime sequence** every consumption request follows through the domain. This is
the behavioral contract that any implementation must obey (it dictates method signatures,
transaction boundaries, and event emissions per aggregate). It is the execution counterpart to the
structural aggregate model in §7 and the domain services in §11.

```
Subscription Resolution
        ↓
Policy Resolution
        ↓
Quota Resolution
        ↓
Debt Resolution
        ↓
Consumption Decision
        ↓
Usage Recording
        ↓
Debt Update
        ↓
Domain Events
        ↓
Outbox
        ↓
Audit
        ↓
OPA Sync
```

| # | Stage | Aggregate / Service | Notes |
|---|-------|---------------------|-------|
| 1 | **Subscription Resolution** | `PrincipalHierarchyReadModel` + `ISubscriptionRepository` | Resolve effective `Subscription`(s) for the consumer's scope (User→Role→Tenant, first-defined wins; §8, §14). |
| 2 | **Policy Resolution** | `IPolicyRepository` + OPA (`RegoModule`) | Load `Policy` referenced by subscriptions; evaluate ABAC conditions. OPA consults `RegoModule` for pure ABAC (§15, ADR-003). |
| 3 | **Quota Resolution** | `QuotaResolutionSpecification` / `IQuotaPolicyRepository` | Walk hierarchy, return effective `QuotaPolicy` (read-only reference, no lock). |
| 4 | **Debt Resolution** | `IDebtLedgerRepository` → `DebtLedger` | Load the SINGLE `OutstandingDebt` per action + per-window `RecoveryPosition`. Debt is NOT window-scoped; any outstanding debt dominates all windows (inv 7). |
| 5 | **Consumption Decision** | `AllowanceEngine` | `Available = RenewedAllowance − OutstandingDebt`. Consumption **always succeeds** (inv 3); never truncate. Any outstanding debt (single per action) blocks all windows (inv 7); recovery is applied lazily on first access after the renewal boundary. |
| 6 | **Usage Recording** | `IUsageLedgerRepository` → `UsageLedger` | Increment per-action `UsageCounter`; raise `UsageRecorded`. |
| 7 | **Debt Update** | `IDebtLedgerRepository` → `DebtLedger` | On exceed, `DebtLedger.IncurDebt(totalExcessForAction)` records the SINGLE (action) debt (NOT per-window cascade); raise `DebtIncurred` + `QuotaExceeded`. |
| 8 | **Domain Events** | `AggregateRoot.RaiseDomainEvent` | Events carry `TenantId` + `CorrelationId` only (ADR-012). |
| 9 | **Outbox** | `UnitOfWorkBehavior` + `DbContext` (§9, ADR-015) | `DomainEvents` → `OutboxMessage` in the same transaction. |
| 10 | **Audit** | Outbox → AuditService (ADR-017) | Audit-relevant events delivered immutably. |
| 11 | **OPA Sync** | PolicyService.Infrastructure (§15, ADR-003) | Publish effective-allowance facts / `RegoModule` to OPA. **Quota/debt thresholds are never encoded in Rego** (§15 rejected). |

**Transaction boundary:** stages 5–7 (decision + usage + debt) are the per-consumer strong-consistency
boundary — `AllowanceEngine` loads `UsageLedger` + `DebtLedger` (and reads `QuotaPolicy` by id) and
persists them in one transaction via `UnitOfWorkBehavior` (§9). Stages 1–4 are reads against the
local read model / repositories (eventually consistent; no runtime call to Authorization/Tenant/Identity).
Stages 8–11 are atomic with the mutation (Outbox in same tx); Audit and OPA Sync are downstream and
must never block consumption.

**Gating rule for implementation:** no Domain class may be written until this pipeline (and its
aggregate/service/event mapping) is approved, because it defines the contracts each aggregate must
expose.
