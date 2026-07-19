# Policy Service — Repository & Specification Usage Report

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`, `01`, `02`.
- **Authoritative inputs:** ADR-015 (transaction ownership), ADR-018 §9 (consistency); reference implementation `AuthorizationService.Infrastructure/Persistence/Repositories/RoleRepository.cs` + `.../Configurations/RoleConfiguration.cs` + `.../AuthorizationDbContext.cs`.

> **Purpose:** define how the 5 domain repository interfaces are realized in Infrastructure, how the `PolicyDbContext` + per-aggregate Unit-of-Work are structured, how EF maps the aggregates (including the **private `Dictionary<>` fields** in the two ledgers — a real implementation risk), and exactly where the 4 domain specifications are (and are not) invoked from the Application layer.

---

## 1. Repository Contract (already defined in Domain)

The domain `IRepositories.cs` exposes five interfaces. The Application layer only ever calls `GetByIdAsync(tenantId, id, trackChanges)`, `GetByScopeAsync`/`ListBy*`, `GetOrCreateAsync` (ledgers), `AddAsync`, `UpdateAsync`. No repository method accepts a `RequestContext` (ADR-012).

| Repository | Key methods |
|------------|-------------|
| `IPolicyRepository` | `GetByIdAsync`, `ListByTenantAsync`, `AddAsync`, `UpdateAsync` |
| `ISubscriptionRepository` | `GetByIdAsync`, `GetByScopeAsync`, `ListByPolicyAsync`, `AddAsync`, `UpdateAsync` |
| `IQuotaPolicyRepository` | `GetByIdAsync`, `GetByScopeAsync`, `ListByScopeAsync`, `AddAsync`, `UpdateAsync` |
| `IUsageLedgerRepository` | `GetByIdAsync`, `GetOrCreateAsync(tenantId, consumerId)`, `UpdateAsync` |
| `IDebtLedgerRepository` | `GetByIdAsync`, `GetOrCreateAsync(tenantId, consumerId)`, `UpdateAsync` |

---

## 2. Implementation Template (from AuthorizationService `RoleRepository`)

Every PolicyService repository follows the exact Authz shape:

```csharp
public sealed class PolicyRepository(PolicyDbContext dbContext) : IPolicyRepository
{
    public Task<Policy?> GetByIdAsync(Guid tenantId, PolicyId policyId, bool trackChanges = false, CancellationToken ct = default) =>
        trackChanges
            ? dbContext.Policies.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == policyId.Id, ct)
            : dbContext.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == policyId.Id, ct);

    public Task AddAsync(Policy policy, CancellationToken ct) => dbContext.Policies.AddAsync(policy, ct);
    public Task UpdateAsync(Policy policy, CancellationToken ct) => Task.CompletedTask; // EF tracks in-UoW; no explicit call
}
```

Rules derived from the reference:

1. **`trackChanges:false` ⇒ `AsNoTracking()`** for all read-only loads (resolvers, queries, and the read-only reference loads inside UC-20/24 per `02` §6).
2. **`AddAsync` ⇒ `dbContext.Set<T>().AddAsync`** (new aggregate, identity `ValueGeneratedNever`).
3. **`UpdateAsync` is a no-op** — the aggregate was loaded tracked inside the `UnitOfWorkBehavior` transaction and EF change-tracking persists it at `SaveChangesAsync` (ADR-015). Callers still invoke it for symmetry/interface conformance.
4. **Tenant scoping is always `TenantId == tenantId`** in the predicate — never a raw `Id` lookup that would bypass tenant isolation (ADR-012/013).
5. **`GetOrCreateAsync`** (ledgers): `FirstOrDefaultAsync` by `(tenantId, consumerId)`; if null, `Policy.Create`/ledger `Create(...)` then `AddAsync`. Idempotent create inside the consumer's transaction (see `02` §3 / risk R-2).

---

## 3. DbContext & Unit-of-Work

`PolicyDbContext : DbContext` mirrors `AuthorizationDbContext`:

- `DbSet<Policy>`, `DbSet<Subscription>`, `DbSet<QuotaPolicy>`, `DbSet<UsageLedger>`, `DbSet<DebtLedger>`, plus `DbSet<OutboxMessage>` (same schema as `SharedKernel OutboxMessage`).
- **Outbox capture in `SaveChangesAsync` override** (verbatim pattern from `AuthorizationDbContext`): scan `ChangeTracker` for `AggregateRoot` entities, pull `DomainEvents`, append `OutboxMessage` rows, call `base.SaveChangesAsync`, then `ClearDomainEvents`. This is what makes the outbox atomic with the aggregates (`02` §4).
- Each aggregate gets a `I*UnitOfWork : ITransactionalUnitOfWork` (e.g. `IPolicyUnitOfWork`) registered as `IUnitOfWork` so `UnitOfWorkBehavior` can `Begin/Save/Commit/Rollback`.

```csharp
services.AddDbContextPool<PolicyDbContext>(...UseNpgsql(...).AddInterceptors(new TenantRlsInterceptor())...);
services.AddScoped<IPolicyRepository, PolicyRepository>();
// ... one per aggregate ...
services.AddScoped<IPolicyUnitOfWork, PolicyUnitOfWork>();
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IPolicyUnitOfWork>());
services.AddScoped<IPlatformOutboxRepository<PolicyDbContext>, OutboxRepository<PolicyDbContext>>();
```

> **Decision:** PolicyService uses the **same** `OutboxRepository<PolicyDbContext>` generic and the `OutboxProcessorBase<PolicyDbContext>` hosted dispatcher already shipped in `SharedKernel`/`Platform` — no custom outbox code required.

---

## 4. EF Mapping Plan

### 4.1 Shared rules (from `RoleConfiguration`)

- `builder.ToTable("policies" | "subscriptions" | "quota_policies" | "usage_ledgers" | "debt_ledgers")`.
- **Composite key** `builder.HasKey(e => new { e.TenantId, e.Id })`.
- `builder.Property(e => e.Id).ValueGeneratedNever();` (and `TenantId` `ValueGeneratedNever()`).
- `builder.Ignore(e => e.DomainEvents);` (events are captured from the live object, never persisted as columns).
- All value objects mapped via `HasConversion` (e.g. `PolicyPriority`, `PolicyCondition`, `PolicyExpression`, `SubscriptionScope`, `Quota`, `ConsumerId`, `ActionKey`, envelope VOs). `enum`s (status, window) as `HasConversion<string>()`.
- `Property(e => e.Version).IsConcurrencyToken();` for optimistic concurrency (`02` §3).

### 4.2 The private-Dictionary risk (R-1 — must spike before P1)

`UsageLedger` and `DebtLedger` store state in **private `Dictionary<,>` fields**, not public collections:

```csharp
// UsageLedger
private readonly Dictionary<UsageKey, UsageCounter> _counters;   // UsageKey = (ActionKey Action, QuotaWindow Window)
// DebtLedger
private readonly Dictionary<DebtKey, DebtAmount>     _debts;
private readonly Dictionary<DebtKey, RecoveryPosition> _recoveries;
```

EF Core **cannot map a `Dictionary<record-struct, owned-VO>` directly** — the dictionary key is not a persistable column set by default. This is the single most important implementation unknown in the Application layer and was not exercised by AuthorizationService (its aggregates use plain columns).

**Recommended approach (to validate in a spike):** map each dictionary as an **owned collection** (`OwnsMany`) into a child table, keyed by `(TenantId, LedgerId, Action, Window)`, with the parent FK and field-only access:

```csharp
builder.OwnsMany<UsageCounter>("_counters", b =>
{
    b.ToTable("usage_counters");
    b.WithOwner().HasForeignKey("UsageLedgerId");
    b.Property(c => c.Action).HasConversion(a => a.Value, v => ActionKey.Create(v));
    b.Property(c => c.Window).HasConversion<string>();
    b.HasKey("UsageLedgerId", "Action", "Window");
    b.Navigation().HasField("_counters").UsePropertyAccessMode(PropertyAccessMode.Field);
});
```

Same pattern for `DebtLedger._debts` (→ `debt_amounts`) and `DebtLedger._recoveries` (→ `recovery_positions`). The `UsageKey`/`DebtKey` record structs are **not** persisted; the `(Action, Window)` VOs serve as the natural key. On materialization EF repopulates the dictionary via the field.

- **Risk if the spike fails:** the ledgers cannot use `Dictionary<>`; fallback is to refactor the domain to expose `IEnumerable<UsageCounter>` with an `Add` mutator — but the domain is **approved/committed**, so this would require a follow-up ADR + re-audit. **Mitigation:** run the mapping spike as the very first task of P1 before any handler code depends on it.
- See `09-review-and-refactor.md` R-1 for the full risk write-up.

### 4.3 Tenant RLS

Register `TenantRlsInterceptor` (same as Authz `AuthorizationDbContext`) so row-level security is enforced at the DB in addition to the repository predicate. Confirm `SharedKernel` exposes the interceptor; if not, the repository predicate alone guarantees isolation.

---

## 5. Specification Usage

The 4 domain specifications are **domain-internal pure predicates**, not repository concerns:

| Specification | Invoked from | Application touchpoint |
|---------------|--------------|------------------------|
| `IQuotaResolutionSpecification` | `IQuotaResolver` | UC-19 (query) — resolver returns effective quota |
| `IDebtDominanceSpecification` | `IAllowanceEngine` (inline) | UC-20 — **engine invokes it internally** |
| `IAllowanceSufficiencySpecification` | `IAllowanceEngine` (inline) | UC-20 — engine internally |
| `IRecoveryEligibilitySpecification` | `IRecoveryProcessor` / handler | UC-24 — handler may pre-check, engine executes |

**Critical finding (domain audit F-1):** `AllowanceEngine.Consume` (observed) implements debt-dominance, allowance-sufficiency, and excess logic **inline** rather than calling the three `*Specification` classes. The specifications exist as documented domain contracts but are bypassed by the engine's runtime path.

**Application-layer decision:**
- The Application layer **must not** re-implement or duplicate this logic. It calls the domain services (`IAllowanceEngine`, `IRecoveryProcessor`, `IQuotaResolver`, `ISubscriptionResolver`) and lets them own the rules.
- The published specifications remain the **canonical reference** for what the engine *should* compute; if a future audit reconciles the engine with the specs, it is a domain-layer change, not an Application change.
- For UC-24 the handler may call `IRecoveryEligibilitySpecification.IsEligible(...)` as a fast no-op guard, but the authoritative mutation is `IRecoveryProcessor.Recover` (which may re-check).
- **Repositories never call specifications.** Specifications never touch persistence.

---

## 6. TODO

- [ ] **P1 spike (R-1):** prove `OwnsMany` + field access maps the three private dictionaries; capture a working `EntityTypeConfiguration` sample.
- [ ] Confirm `TenantRlsInterceptor` availability in `SharedKernel`/Platform; otherwise rely on repository predicate.
- [ ] Implement `PolicyDbContext` + 5 `IEntityTypeConfiguration` classes + 5 repositories + 5 `I*UnitOfWork` (P1).
- [ ] Wire `OutboxRepository<PolicyDbContext>` + `OutboxProcessorBase<PolicyDbContext>` hosted service (P1).
- [ ] Decide whether `GetOrCreateAsync` concurrency uses a unique `(tenantId, consumerId)` constraint + optimistic retry (R-2).

**Next:** `04-integration-boundary-report.md`.
