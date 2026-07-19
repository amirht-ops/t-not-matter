# PolicyService — Debt & Recovery Schema Verification

- **Date:** 2026-07-16
- **Author:** Enterprise Architecture Reviewer
- **Trigger:** Confirm that the deployed/declared database schema already matches the approved single-liability Debt & Recovery model defined in `docs/policy-review/02-debt-recovery-design-review.md` (correction to M2, ADR-018 amendment).
- **Companion docs:** `00-production-readiness.md`, `01-remediation-plan.md`, `02-debt-recovery-design-review.md`, `docs/adr/ADR-018-policy-quota-debt-recovery-domain.md`.
- **Status:** Verified. **No schema change, no EF migration, and no data migration are required.**

---

## 1. Purpose

The authoritative business rule (design-review §1, ADR-018 §3.5/§3.6) states that **debt is NOT attached to a quota window** — it is a single outstanding liability per (consumer, action), denominated in allowance units, repaid by any window's renewal. The window axis must exist only in usage tallies (`UsageLedger`/`usage_counters`) and in the per-window post-repayment usable allowance (`RecoveryEntry`/`recovery_entries`).

The original implementation (and the per-window migration that *would* have been required) was corrected in code. Before declaring the model consistent, we must formally verify that the **database schema** (as expressed through the domain model, EF mapping, EF model snapshot, and the InitialCreate migration) already reflects the single-liability model — i.e. that `debt_entries` carries **no** `window` column while `recovery_entries` and `usage_counters` **retain** their window axis.

This document establishes, by evidence, that the schema is already synchronized with the approved model, so no migration is produced or required.

---

## 2. Current Domain Model (Approved)

Source of truth: `docs/policy-review/02-debt-recovery-design-review.md` §3.1–§3.5.

- **Debt** is a **single `DebtAmount` outstanding liability per (consumer, action)**. There is **no window dimension**. It survives rollover and is reduced only by recovery. (`DebtLedger.cs:26`, `DebtLedger.cs:102` — `OutstandingDebt(ActionKey)`.)
- **`DebtEntry`** (owned entity / `debt_entries` row) carries only `Action` + `Amount`. It has **no `Window` property**. (`DebtEntry.cs:13-39`.)
- **Usage** remains per (consumer, action, window). `UsageLedger` tallies reset on each window's rollover. (`UsageCounter` VO + `usage_counters` table.)
- **Recovery** is **per renewal event**: any window (Daily/Weekly/Monthly) renewing contributes its renewed allowance toward the single debt first (`repaid = min(renewedAllowance, outstandingDebt)`), and records that window's post-repayment usable allowance as a `RecoveryPosition`.
- **`RecoveryEntry`** (owned entity / `recovery_entries` row) carries `Action`, `Window`, `Position` (remaining), and `WindowEnd`. It **retains `Window`** because it is the per-window usable allowance, distinct from the (window-agnostic) debt. (`RecoveryEntry.cs:16-34`.)
- **Dominance (invariant 7):** while `OutstandingDebt(action) > 0`, no renewed allowance for any window is usable for that action.

No new aggregate is introduced; the `UsageLedger`/`DebtLedger` boundary and the `UnitOfWorkBehavior` transaction boundary (ADR-015) are unchanged.

---

## 3. EF Mapping Verification

Files verified: `DebtEntry.cs`, `RecoveryEntry.cs`, `DebtLedgerConfiguration.cs`, `UsageCounter` mapping (snapshot).

### 3.1 `DebtEntry` has no `Window` property — ✅ PASS

`src/Services/PolicyService/PolicyService.Domain/ValueObjects/DebtEntry.cs`:

```csharp
public ActionKey Action { get; private set; } = null!;
public DebtAmount Amount { get; private set; } = null!;
```

There is no `Window` field, property, or constructor parameter. The class XML doc explicitly states *"Debt is NOT attached to a quota window … it is one outstanding liability per (consumer, action)."* (lines 7-12).

### 3.2 `DebtLedgerConfiguration` uses `(DebtLedgerId, Action)` as the key — ✅ PASS

`src/Services/PolicyService/PolicyService.Infrastructure/Persistence/Configurations/DebtLedgerConfiguration.cs:28-42`:

```csharp
builder.OwnsMany(ledger => ledger.Debts, debt =>
{
    debt.ToTable("debt_entries");
    debt.WithOwner().HasForeignKey("DebtLedgerId");
    debt.Property(d => d.Action).HasColumnName("action") ... ;
    debt.Property(d => d.Amount).HasColumnName("amount") ... ;
    debt.HasKey("DebtLedgerId", "action");
});
```

The owned collection is keyed by `(DebtLedgerId, action)` — **no `window` column is mapped**. The `window` property is absent and is not converted or configured anywhere in this block.

### 3.3 `RecoveryEntry` still contains `Window` — ✅ PASS

`src/Services/PolicyService/PolicyService.Domain/ValueObjects/RecoveryEntry.cs:29`:

```csharp
public QuotaWindow Window { get; private set; }
```

The EF configuration (`DebtLedgerConfiguration.cs:44-65`) maps it explicitly:

```csharp
recovery.Property(r => r.Window)
    .HasColumnName("window")
    .HasConversion<string>()
    .IsRequired();
recovery.HasKey("DebtLedgerId", "action", "window");
```

So `recovery_entries` correctly retains its `window` column and `(DebtLedgerId, action, window)` key.

### 3.4 Usage counters remain window-based — ✅ PASS

The `UsageLedger` owns `UsageCounter` entities mapped to `usage_counters` (`PolicyDbContextModelSnapshot.cs`, `usage_counters` block). The snapshot shows:

```csharp
b1.Property<string>("Action").HasColumnName("action");
b1.Property<string>("Window").HasColumnName("window");
b1.Property<long>("Count").HasColumnName("count");
b1.Property<DateTimeOffset>("WindowStart").HasColumnName("window_start");
b1.Property<DateTimeOffset>("WindowEnd").HasColumnName("window_end");
b1.HasKey("Action", "Window");
b1.ToTable("usage_counters", "policy");
```

Usage remains per (action, window) with window boundaries — exactly as the model requires.

**Result:** EF mapping is fully consistent with the approved single-liability model.

---

## 4. Migration Verification

Four artifacts are compared for equal representation of the debt/recovery schema:

| Artifact | Location | `debt_entries` schema | `recovery_entries` schema | `usage_counters` schema |
|----------|----------|-----------------------|---------------------------|-------------------------|
| Domain Model | `DebtEntry.cs`, `RecoveryEntry.cs`, `DebtLedger.cs` | `Action`, `Amount` only | `Action`, `Window`, `Position`, `WindowEnd` | (usage VO) `Action`, `Window`, `Count`, `WindowStart`, `WindowEnd` |
| EF Mapping | `DebtLedgerConfiguration.cs` | `(DebtLedgerId, action)`; no `window` | `(DebtLedgerId, action, window)`; `window` present | `(Action, Window)`; `window` present |
| Model Snapshot | `PolicyDbContextModelSnapshot.cs` | `HasKey("Action")`; cols `action`, `amount`, `DebtLedgerId`, `TenantId` | `HasKey("Action", "Window")`; `window` present | `HasKey("Action", "Window")`; `window` present |
| InitialCreate migration | `20260716091027_InitialCreate.cs` | `PK_debt_entries` on `action`; cols `action`, `amount`, `DebtLedgerId`, `TenantId` | `PK_recovery_entries` on `action, window`; `window` present | `PK_usage_counters` on `action, window`; `window` present |

### Why they already match

The `InitialCreate` migration was generated **after** the code was corrected to the single-liability model. Therefore:

1. `DebtEntry` had already lost its `Window` when the migration was scaffolded, so `debt_entries` was created **without** a `window` column — PK on `action` only (plus the FK `DebtLedgerId`/`TenantId` for ownership).
2. `RecoveryEntry` still carried `Window`, so `recovery_entries` was created **with** the `window` column and a composite `(action, window)` PK.
3. `UsageCounter` was always window-based, so `usage_counters` retains `(action, window)`.

Because the migration, snapshot, EF mapping, and domain model were all produced from the same corrected code baseline, they are mutually consistent by construction. No intermediate per-window `debt_entries` schema was ever deployed.

---

## 5. Generated Migration Verification

A new migration was generated to empirically confirm schema synchronization:

```
dotnet ef migrations add DebtNoWindowCheck \
  --project PolicyService.Infrastructure \
  --startup-project PolicyService.Api \
  --context PolicyDbContext
```

The resulting `20260716091354_DebtNoWindowCheck.cs` was an **empty migration**:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
}

protected override void Down(MigrationBuilder migrationBuilder)
{
}
```

EF Core compares the *current* in-code model against the *last snapshot* (`PolicyDbContextModelSnapshot.cs`). An empty `Up`/`Down` means EF detected **zero differences** between the domain/EF model and the snapshot — i.e. no table, column, key, or index change is required.

This empty migration was then removed (`dotnet ef migrations remove`) so that **no no-op migration is left in the repository**:

```
Removing migration '20260716091354_DebtNoWindowCheck'.
Reverting the model snapshot.
Done.
```

**Conclusion:** Generating a migration that produces an empty `Up`/`Down` is the authoritative proof that the database schema (as declared by `InitialCreate` + snapshot) is already synchronized with the approved domain model. No migration artifact is warranted.

---

## 6. Data Migration Analysis

### Is a data migration required? — NO.

The `InitialCreate` migration created `debt_entries` **without** a `window` column. The running/provisioned schema has therefore **never** carried per-window debt rows. There are no historical per-(action, window) debt rows to consolidate, so **no backfill or data transformation is required**.

The per-window→single-liability debt restructuring described in design-review §9 TODO #2 (and the backfill it mentions) is only necessary when migrating **from** an older per-window `debt_entries` schema. Since this codebase's first and only debt schema is already single-liability, that scenario does not apply.

### Hypothetical backfill procedure (only if an older per-window schema had been deployed)

Documented for completeness / disaster-recovery reference. **Not executed, not required here.**

If a prior `debt_entries` had carried a `window` column (per (action, window) debt), the consolidation to single (action) debt would be:

```sql
-- 1. Compute the consolidated single (action) debt per ledger.
SELECT "DebtLedgerId", "action", SUM("amount") AS total_debt
INTO #consolidated
FROM policy.debt_entries
GROUP BY "DebtLedgerId", "action";

-- 2. Remove the now-obsolete per-window rows (the window column is being dropped).
DELETE FROM policy.debt_entries;

-- 3. Insert one consolidated row per (DebtLedgerId, action).
INSERT INTO policy.debt_entries ("action", "amount", "DebtLedgerId", "TenantId")
SELECT c."action", c.total_debt, c."DebtLedgerId", l."TenantId"
FROM #consolidated c
JOIN policy.debt_ledgers l
  ON l."TenantId" = c."TenantId" AND l."Id" = c."DebtLedgerId"
WHERE c.total_debt > 0;

-- 4. Drop the window column (paired with the EF migration that removes it).
ALTER TABLE policy.debt_entries DROP COLUMN "window";
```

Constraints to respect during such a hypothetical backfill:
- Execute inside the same tenant-scoped transaction boundary; preserve `TenantId` on every row (B3 — RLS/tenant filter unchanged).
- The consolidation is additive across windows, so total outstanding debt is preserved (no loss of liability).
- `recovery_entries` and `usage_counters` are **not** touched — their `window` axis is retained.

Again: **this procedure is documented only for reference; it was not run and is not needed.**

---

## 7. Upgrade Safety

### Backward compatibility
- The schema is additive-only in spirit: `debt_entries` has **fewer** columns than a hypothetical per-window design (no `window`), which is a strict subset of any compatible shape. Reads/writes go through the same `DebtLedger` aggregate and `DebtLedgerConfiguration`; no consumer code path references a `window` on debt.
- `recovery_entries` and `usage_counters` are unchanged in shape, so existing recovery/usage queries remain valid.

### Rollback impact
- Because **no migration is applied**, there is nothing to roll back at the database layer. A code rollback to any prior single-liability commit is fully schema-compatible (the schema never diverged).
- If a future bad migration were ever introduced, rollback is a standard `ef database update <previous>` — but none is warranted now.

### Production deployment impact
- **Zero database downtime / zero DDL.** Deploying the corrected PolicyService code against the existing `InitialCreate` schema requires no `dotnet ef database update` step.
- The outbox/event capture path (`PolicyDbContext.SaveChangesAsync` override, ADR-015/017) is unchanged, so event atomicity and tenant isolation (B3/B4) are unaffected.
- Deployment is a pure code/assembly rollout; the DB contract is already satisfied.

---

## 8. Final Conclusion

Formal verification establishes that the database schema already represents the approved single-liability Debt & Recovery model:

- ✅ **No schema changes are required.** `debt_entries` has no `window` column; `recovery_entries` and `usage_counters` retain their window axis.
- ✅ **No EF migration is required.** Generating a new migration yields an empty `Up`/`Down`, proving model == snapshot == `InitialCreate`. The empty migration was removed and is **not** committed.
- ✅ **No data migration is required.** The running schema was created single-liability from the start; there are no per-window debt rows to backfill.
- ✅ **Only documentation was updated.** This verification document (`03-schema-verification.md`) is the sole deliverable; no code, mapping, migration, or data was modified.

The Debt & Recovery persistence layer is therefore confirmed consistent with `docs/policy-review/02-debt-recovery-design-review.md` and the ADR-018 single-liability amendment.
