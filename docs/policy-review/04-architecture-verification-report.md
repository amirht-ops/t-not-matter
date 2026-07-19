# ADR-018 Architecture Verification Report — Debt & Recovery Domain

- **Date:** 2026-07-16
- **Scope:** `PolicyService.Domain` — `QuotaPolicy`, `UsageLedger`, `DebtLedger`, `AllowanceEngine`, `RecoveryProcessor`, Specifications.
- **Reference ADR:** `docs/adr/ADR-018-policy-quota-debt-recovery-domain.md` (Approved 2026-07-16).
- **Verification method:** executable xUnit specs (`tests/PolicyService.Domain.Tests`) + direct code citation. Full build: **0 errors**. Full domain test run: **33 passed / 0 failed**.
- **Approved model under test:** single outstanding debt **per (Consumer, Action)**; no window axis on debt; `RecoveryPosition` persisted **per (Consumer, Action, Window)**; lazy first-access recovery on **any** Daily/Weekly/Monthly renewal; **no scheduler** (`RecoveryProcessorJob` removed).

---

## 1. Method

Every invariant in ADR-018 §8 is mapped below to (a) the enforcing code and (b) the concrete test(s) that prove it. Each scenario in the request (300/320/460/600/760 units, multiple consecutive roll-overs, simultaneous Daily/Weekly/Monthly roll-over, repeated-recovery idempotency) is pinned by a named test.

> **Clock note.** `AllowanceEngine.Consume` reads `DateTimeOffset.UtcNow` and detects an elapsed renewal via `DebtLedger.IsRecoveryDue(action, window, boundaryStart)`. To make roll-over deterministic in tests, the ledger's recovery marker is seeded with a **past** `WindowEnd` using `DebtLedger.Recover(...)`, which is the same method the engine calls lazily — so the seeded markers faithfully reproduce what a real elapsed boundary produces. Direct-recovery scenarios call `DebtLedger.Recover` at strictly advancing boundaries; engine-integration scenarios drive recovery through `AllowanceEngine.Consume` exactly as production does.

---

## 2. ADR-018 Invariants — Evidence Matrix

| # | Invariant (ADR-018 §8) | Enforcing code | Test(s) — file `tests/PolicyService.Domain.Tests/…` |
|---|------------------------|----------------|------------------------------------------------------|
| 1 | **Allowance is non-negative.** No window allowance is ever negative. | `QuotaLimit.Create` rejects `value < 0` (`ValueObjects/QuotaLimit.cs:13`). | `DebtAlgorithmScenariosTests.Invariant1_AllowanceLimitRejectsNegative` |
| 2 | **Debt is separate and non-negative.** Never modelled as negative quota. | `DebtAmount.Create` rejects `< 0` (`ValueObjects/DebtAmount.cs:16`); `DebtEntry` has **no window field** and is stored in its own `Debts` collection, not in usage (`DebtLedger.cs:26`). | `DebtAlgorithmScenariosTests.Invariant2_DebtAmountRejectsNegative_AndSeparateFromUsage`, `DebtLedgerTests.IncurDebt_AccumulatesSingleLiabilityPerAction_NotPerWindow` |
| 3 | **Consumption always succeeds.** Full result set; never truncated. | `AllowanceEngine.Consume` returns `Allow` for any over-limit op and only records debt (`AllowanceEngine.cs:94-95`); there is no truncation branch. | `DebtAlgorithmScenariosTests.Invariant3_ConsumptionAlwaysSucceeds_NeverTruncated`, `AllowanceEngineDebtRecoveryTests.Consume_ExceedingMonthlyLimit_IncurrsSingleDebt_AndPersistsRecoveryPositions` |
| 4 | **Excess becomes debt.** Over-limit excess recorded as debt in the same operation. | `DebtLedger.IncurDebt` raises `DebtIncurredDomainEvent` + `QuotaExceededDomainEvent` (`DebtLedger.cs:51-64`); engine calls it in the same `Consume` (`AllowanceEngine.cs:87-92`). | `AllowanceEngineDebtRecoveryTests.Consume_ExceedingMonthlyLimit_IncurrsSingleDebt_AndPersistsRecoveryPositions`, `DebtAlgorithmScenariosTests.Consume_320/460/600/760_*` |
| 5 | **Single debt per action, survives rollover.** Not attached to a window; boundaries do not erase it. | `OutstandingDebt(action)` returns the one `DebtEntry` per action (`DebtLedger.cs:102-106`); `Recover` never clears debt unless fully repaid; no window coupling. | `DebtLedgerTests.IncurDebt_AccumulatesSingleLiabilityPerAction_NotPerWindow`, `DebtLedgerTests.IncurDebt_KeepsActionsIndependent`, `DebtLedgerTests.Recover_AnyWindowRepaysSameDebt` |
| 6 | **Recovery deducts debt first.** `Available = RenewedAllowance − OutstandingDebt`. Full allowance only after debt = 0. | `DebtLedger.Recover` repays `min(renewed, debt)` then sets `RecoveryPosition = renewed − repaid` (`DebtLedger.cs:72-92`); engine applies this lazily before evaluation (`AllowanceEngine.cs:47-57`). | `DebtLedgerTests.Recover_RepaysSingleDebt_AndPersistsRecoveryPosition_ForTheWindow`, `DebtLedgerTests.Recover_Partial_LeavesDebtAndZeroRecoveryPosition`, `DebtAlgorithmScenariosTests.SimultaneousDailyWeeklyMonthlyRollOver_AllAppliedOnce_DebtDeductedFirst` |
| 7 | **Any outstanding debt dominates.** `OutstandingDebt(action) > 0` blocks all windows; single per-action liability, not window-scoped. | `DebtDominanceSpecification.IsDominant(debt) => debt.Value > 0` (`Specifications/DebtDominanceSpecification.cs:8`); engine gates before recording usage (`AllowanceEngine.cs:61-65`). | `AllowanceEngineDebtRecoveryTests.Consume_WhileDebtOutstanding_IsDenied`, `DebtLedgerTests.Dominance_AnyOutstandingDebtDominatesAllWindows` |
| 8 | **Resolution order is fixed.** Effective quota = first defined among User → Role → Tenant. | `QuotaResolutionSpecification.Resolve` orders by `(int)Scope.Kind` descending (User=3 > Role=2 > Tenant=1) (`Specifications/QuotaResolutionSpecification.cs:15-18`). | `DebtAlgorithmScenariosTests.Invariant8_QuotaResolutionPicksHighestPrecedenceScope` |
| 9 | **Reset ≠ reconfiguration.** Reset clears usage/debt/recovery; never alters quota policy. | `DebtLedger.Reset` clears `Debts` + `Recoveries` only, raises `DebtResetDomainEvent` (`DebtLedger.cs:119-126`); `QuotaPolicy` is a separate aggregate untouched by reset. | `DebtLedgerTests.Reset_ClearsDebtAndRecovery_ButNothingElse` |
| 10 | **Distinct concepts.** Quota definition ≠ usage ≠ debt ≠ recovery ≠ reset; each its own aggregate/service. | `QuotaPolicy`, `UsageLedger`, `DebtLedger` are three separate aggregate roots; `AllowanceEngine`/`RecoveryProcessor` are domain services (`Aggregates/*`, `Services/*`). No `RecoveryProcessorJob` exists anywhere (only `OutboxProcessor` hosted service). | `DebtAlgorithmScenariosTests.Invariant10_DistinctAggregatesForPolicyUsageDebt` + code layout (`src/.../Aggregates/{QuotaPolicies,UsageLedgers,DebtLedgers}`) |

### Invariant 7 — how the test proves dominance
`Consume_WhileDebtOutstanding_IsDenied` first incurs a 160 debt (460-unit op). A second op of 1 unit returns `Deny` with reason `ConsumptionBlockedByDebt`, **and** the debt is unchanged at 160 with no additional usage recorded — proving the gate short-circuits before mutation (debt is the single blocking liability, not a per-window cap). `Dominance_AnyOutstandingDebtDominatesAllWindows` directly asserts `IsDominant(1) == true` for any window and `false` after reset.

### Invariant 6 — how the test proves debt-first deduction
`Recover_RepaysSingleDebt_AndPersistsRecoveryPosition_ForTheWindow` incurs 160, then applies a Monthly renewal of 300. Debt → 0, and `RecoveryPosition.Monthly.Remaining = 140` (300 − 160). The 140 is precisely the "available after debt-first deduction" value, and a `DebtRecovered` event carries `AmountRecovered=160, RemainingDebt=0`. `Recover_Partial_LeavesDebtAndZeroRecoveryPosition` proves the opposite: a 20 daily renewal on 160 debt leaves `RecoveryPosition.Daily = 0` (usable allowance fully consumed by debt) while debt falls to 140.

---

## 3. Debt Algorithm — Scenario Evidence

All scenarios use the canonical quota **Daily 20 / Weekly 100 / Monthly 300**. Debt is the **single Monthly overage** `max(0, monthlyCount − 300)`; the engine derives it as a delta (`AllowanceEngine.cs:83-92`) so a non-incremental op is never triple-counted across the nested windows.

| Scenario | Expected single debt | Test — file `tests/PolicyService.Domain.Tests/DebtAlgorithmScenariosTests.cs` | Result |
|----------|----------------------|-------------------------------------------------------------------------------|--------|
| Consume **300** | 0 (within limit) | `Consume_300_WithinMonthlyLimit_IncurrsNoDebt` | Pass |
| Consume **320** | 20 | `Consume_320_ExceedsMonthlyLimit_IncurrsSingleDebt20` | Pass |
| Consume **460** | 160 | `Consume_460_ExceedsMonthlyLimit_IncurrsSingleDebt160` | Pass |
| Consume **600** | 300 | `Consume_600_ExceedsMonthlyLimit_IncurrsSingleDebt300` | Pass |
| Consume **760** | 460 | `Consume_760_ExceedsMonthlyLimit_IncurrsSingleDebt460` | Pass |
| Non-incremental (not triple-counted) | 460 op → 160 (not 460) | `Consume_NonIncremental_IsNotTripleCounted_AcrossWindows` | Pass |
| **Multiple consecutive roll-overs** (incremental repayment) | 160 → 140 → 120 → 100 → 80 over 4 daily renewals | `MultipleConsecutiveRollOvers_RepayDebtIncrementally` | Pass |
| **Multiple consecutive roll-overs** (full repayment → allowance usable) | 300 → … → 0, then `Allow` | `MultipleConsecutiveRollOvers_FullyRepaid_AllowanceUsableAgain` | Pass |
| **Simultaneous** Daily+Weekly+Monthly roll-over | 160 debt, renewed 420 → 0; positions Daily 0 / Weekly 0 / Monthly 260 | `SimultaneousDailyWeeklyMonthlyRollOver_AllAppliedOnce_DebtDeductedFirst` | Pass |
| **Idempotent recovery** (repeated execution) | re-application at same boundary is skipped; debt unchanged | `Recovery_IsIdempotent_SameBoundaryIsNotDueAgain_AfterRecovery` + `AllowanceEngineDebtRecoveryTests.RecoveryProcessor_WhenNoDebt_PersistsFullRecoveryPosition_AndRaisesNoRecoveredEvent` | Pass |

### Idempotency proof
Idempotency is enforced by `DebtLedger.IsRecoveryDue(action, window, boundaryStart) => entry is null || entry.WindowEnd < boundaryStart` (`DebtLedger.cs:95-99`). The engine only calls `RecoveryProcessor.Recover` when `IsRecoveryDue` is true (`AllowanceEngine.cs:50`). `Recovery_IsIdempotent_SameBoundaryIsNotDueAgain_AfterRecovery` applies a Daily renewal once (debt 160 → 140, marker set), then asserts `IsRecoveryDue(..., sameBoundary) == false` and that the debt remains 140 — proving a repeated call at the same period is a no-op. `RecoveryProcessor_WhenNoDebt_...` proves that when no debt exists a renewal persists the full `RecoveryPosition` but raises **no** `DebtRecovered` event, so re-processing an already-cleared period cannot create phantom repayments.

### Simultaneous roll-over proof
The test applies Daily (20), Weekly (100), Monthly (300) renewals once each (total renewed 420 ≥ 160 debt). Final debt = 0. The per-window `RecoveryPosition` shows debt-first deduction from the **same single debt**: Daily 20 → 0 (debt 160→140), Weekly 100 → 0 (140→40), Monthly 300 → 260 (300 − 40). This confirms (a) all three windows' renewals are applied, (b) they all repay the one shared liability, and (c) `RecoveryPosition` is correctly persisted per window.

---

## 4. Lazy First-Access Recovery (ADR-018 §11b) — Evidence

- **No scheduler.** `RecoveryProcessorJob` / `AddHostedService<...Recovery...>` / `: BackgroundService` do not exist in `PolicyService`. The only hosted service is `OutboxProcessor` (ADR-015 outbox dispatch), confirmed by `PolicyService.Infrastructure/DependencyInjection.cs:51`.
- **Lazy trigger.** `AllowanceEngine.Consume` runs the per-window `IsRecoveryDue` → `Recover` loop **before** the dominance gate (`AllowanceEngine.cs:43-57`), i.e. on first access after a boundary, inside the same transaction as the consumption.
- **Persisted `RecoveryPosition`.** `DebtLedger.Recover` always upserts `RecoveryEntry` (`DebtLedger.cs:143-154`); `AllowanceSufficiencySpecification` evaluates the persisted `RecoveryPosition`, not a transient calc (`Specifications/AllowanceSufficiencySpecification.cs:8`).
- **Tests:** `AllowanceEngineDebtRecoveryTests.Consume_AfterDailyAndWeeklyRollovers_ProcessesMultipleElapsedRenewals` (multiple elapsed renewals processed in one access), `Consume_AfterMonthlyRollover_RepaysOldDebt_AllowanceBecomesUsableAgain`, `Consume_AfterDailyRollover_PartiallyRepays_StillDominant`, `AllowanceSufficiencySpecification_UsesPersistedRecoveryPosition`.

---

## 5. Build & Test Verification

```
dotnet build src/Services/PolicyService/PolicyService.Api/PolicyService.Api.csproj
  Build succeeded. 0 Error(s).  (warnings: NU1903 package advisory + 1 pre-existing CS9113
  in UpsertPrincipalEdgeCommand.cs — both unrelated to debt/recovery changes)

dotnet test tests/PolicyService.Domain.Tests/PolicyService.Domain.Tests.csproj
  Passed!  Failed: 0, Passed: 33, Skipped: 0, Total: 33
```

| Test file | Tests | Covers |
|-----------|-------|--------|
| `DebtLedgerTests.cs` | 11 | DebtLedger invariants 2,4,5,6,7,9 + idempotent-enough recovery semantics |
| `AllowanceEngineDebtRecoveryTests.cs` | 6 | Engine lazy recovery, dominance (7), persistence (6), idempotency edge |
| `DebtAlgorithmScenariosTests.cs` | 16 | All requested consumption scenarios + consecutive/simultaneous roll-overs + idempotency + invariants 1,2,3,8,10 |

---

## 6. Residual Risks / Notes

1. **Debt denomination unbounded (ADR-018 §17 open Q4).** Code enforces no ceiling; a consumer can accrue unbounded debt. Business acceptance still open.
2. **`RecoveryPosition.Remaining` may be negative** (`RecoveryPosition.cs:5-8`) to signal debt still exceeding renewed allowance; consumers of `RecoveryPosition` must treat `< 0` as "no usable allowance". `AllowanceSufficiencySpecification` already does (`>= requested` → false when negative).
3. **Clock coupling.** `AllowanceEngine` uses `DateTimeOffset.UtcNow` directly; production deployment must ensure host clocks are synchronized (same caveat as any rollover system). Test determinism is achieved by seeding past markers.
4. **No integration/EF-persistence test** in this suite; verification is at the domain-model layer. The EF mapping (`DebtLedgerConfiguration`) was verified separately in `docs/policy-review/03-schema-verification.md` to carry no Window column on `debt_entries`.
