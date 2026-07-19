# Policy Service — Aggregate Discovery Report

Derived from business rules + ADRs (no prototype assumptions). Follows Vernon aggregate design and `AuthorizationService.Domain` / `IdentityService.Domain` conventions.

## Aggregate 1 — Policy (ABAC definition)

- **Kind:** Aggregate (entity with identity + lifecycle). Root: `Policy`. Optional child: `PolicyVersion` (value-list of versions).
- **Identity:** `PolicyId` (Guid).
- **Value Objects:** `PolicyCondition`, `PolicyExpression`, `PolicyPriority`, `ResourceType`, `ResourceId`, `RegoModule`.
- **State:** Status (Draft→Published→Archived), conditions, compiled Rego (immutable), priority, effective dates, `IsDeleted`.
- **Lifecycle:** Create (Draft) → Validate → Compile (produce `RegoModule`) → Publish (Active) → Archive. Soft-delete via `IsDeleted`.
- **Invariants:** draft-only edits; published policy immutable (new version to change); non-empty conditions; valid Rego.
- **Events:** `PolicyCreated`, `PolicyPublished`, `PolicyArchived`, `PolicyConditionChanged`.
- **Repository:** `IPolicyRepository`.
- **Justification:** independent config lifecycle; referenced by Subscription by id; never holds usage.

## Aggregate 2 — Subscription (policy binding + scope)

- **Kind:** Aggregate. Root: `Subscription`.
- **Identity:** `SubscriptionId` (Guid).
- **Value Objects:** `SubscriptionScope` (Tenant/Role/User + principal id), `PolicyId` (ref), `PolicyPriority`.
- **State:** scope, policyId, status (Assigned→Active→Revoked), precedence rank, effective dates, `IsDeleted`.
- **Lifecycle:** Assign (Draft) → Activate → Revoke / Supersede. Supersede replaces with higher precedence.
- **Invariants:** user→role→tenant precedence deterministic; a scope resolves to exactly one effective subscription per policy (first-defined wins); cannot subscribe to a non-existent/published-only policy reference.
- **Events:** `SubscriptionAssigned`, `SubscriptionActivated`, `SubscriptionRevoked`, `SubscriptionSuperseded`.
- **Repository:** `ISubscriptionRepository` (`GetByScopeAsync`).
- **Justification:** distinct lifecycle from Policy; unit of hierarchical resolution; references Policy by id only.

## Aggregate 3 — QuotaPolicy (quota definition)

- **Kind:** Aggregate. Root: `QuotaPolicy`.
- **Identity:** `QuotaPolicyId` (Guid).
- **Value Objects:** `Quota` (the per-window limits — VO), `QuotaWindow` (enum), `SubscriptionScope`.
- **State:** scope, quotas `{Daily, Weekly, Monthly}`, status, effective dates, `IsDeleted`.
- **Lifecycle:** Define → (effective) → Amend limits/scope → Remove.
- **Invariants:** limits non-negative; at least one window defined; monthly ≥ weekly ≥ daily (consistency of cascade).
- **Events:** `QuotaPolicyDefined`, `QuotaPolicyAmended`, `QuotaPolicyRemoved`.
- **Repository:** `IQuotaPolicyRepository` (`GetByScopeAsync`).
- **Justification:** `Quota` is a VO (no identity/lifecycle); the *policy* is the aggregate. Separate from usage/debt (different lifecycle). Hierarchical (resolved User→Role→Tenant).

## Aggregate 4 — UsageLedger (operational consumption)

- **Kind:** Aggregate. Root: `UsageLedger`.
- **Identity:** `UsageLedgerId` (Guid); natural key `(TenantId, ConsumerId, ActionKey)`.
- **Value Objects:** `UsageCounter` (count + window range), `ActionKey`, `ConsumerId`.
- **State:** collection of `UsageCounter` per window per action; open/closed; `IsDeleted`.
- **Lifecycle:** lazy creation on first consumption → open → window reset on rollover → admin Reset → soft-delete on consumer offboard.
- **Invariants:** counters non-negative; independent per action; reset clears counters; never blocks (consumption always succeeds).
- **Events:** `UsageRecorded`, `UsageReset`.
- **Repository:** `IUsageLedgerRepository` (`GetOrCreateAsync`).
- **Justification:** high-churn operational state; separate from config (Phase 4); per-action independent counters; reset lifecycle differs from debt.

## Aggregate 5 — DebtLedger (liability + recovery)

- **Kind:** Aggregate. Root: `DebtLedger`.
- **Identity:** `DebtLedgerId` (Guid); natural key `(TenantId, ConsumerId)`.
- **Value Objects:** `Debt` (non-negative outstanding), `RecoveryPosition`, `ActionKey`.
- **State:** per-action outstanding debt; recovery positions; open/closed; `IsDeleted`.
- **Lifecycle:** lazy creation on first excess → open → recovery on rollover → admin Reset → soft-delete on offboard.
- **Invariants:** debt never negative; debt survives rollover; recovery deducts debt first (`Available = Renewed − Debt`); no access while debt > 0; any outstanding debt dominates all windows. Debt is a single per-(action) liability — NOT window-scoped.
- **Events:** `DebtIncurred`, `DebtRecovered`, `DebtReset`, `QuotaExceeded`.
- **Repository:** `IDebtLedgerRepository` (`GetOrCreateAsync`).
- **Justification:** separate lifecycle from usage (survives reset/rollover); independent liability; recoverable/auditable; not embedded in quota or usage.

## Summary table

| Aggregate | Identity | Lifecycle volatility | Key references | Repo |
|-----------|----------|----------------------|----------------|------|
| Policy | PolicyId | low | — | IPolicyRepository |
| Subscription | SubscriptionId | low | PolicyId | ISubscriptionRepository |
| QuotaPolicy | QuotaPolicyId | low | scope | IQuotaPolicyRepository |
| UsageLedger | UsageLedgerId | high | ConsumerId, ActionKey | IUsageLedgerRepository |
| DebtLedger | DebtLedgerId | medium | ConsumerId | IDebtLedgerRepository |

## Explicitly NOT aggregates (Value Objects)

`Quota`, `UsageCounter`, `Debt`, `RecoveryPosition`, `ActionKey`, `ConsumerId`, `TenantId`, `RoleId`, `DepartmentId`, `UserId`, `ResourceType`, `ResourceId`, `PolicyCondition`, `PolicyExpression`, `PolicyPriority`, `RegoModule`, `SubscriptionScope`, `QuotaWindow`.
