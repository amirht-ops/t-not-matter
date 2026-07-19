# PolicyService — Debt & Recovery Design Review (M2 correction)

- **Date:** 2026-07-15
- **Author:** Enterprise Architecture Reviewer
- **Trigger:** Correction to the M2 finding in `00-production-readiness.md`. The prior review assumed debt recovery should be *per-window* (Daily/Weekly/Monthly). The authoritative business rule states debt is a **single outstanding liability**, and recovery is **per renewal event** (any window's renewal repays the single debt).
- **Status:** Design review **APPROVED** (2026-07-16). The architecture decisions in §11 below are ratified as the canonical implementation model. The codebase has been corrected to match this model (see `03-schema-verification.md`).
- **Companion docs:** `00-production-readiness.md`, `01-remediation-plan.md`, `docs/adr/ADR-018-policy-quota-debt-recovery-domain.md`.

---

## 1. Authoritative business rule (as given)

- Three linked allowance windows: **Daily / Weekly / Monthly** (e.g. 20 / 100 / 300).
- Consumption **never truncates** — a 460-result operation returns all 460; the excess (460 − 300 = 160) becomes **debt**.
- **Debt is NOT attached to a window.** It is one outstanding liability, denominated in the same units as allowance.
- Windows only determine **WHEN new allowance appears**; debt determines **WHETHER that allowance is usable**.
- **Recovery = per renewal event, not per window:**
  - `availableAllowance = renewedAllowance − outstandingDebt`
  - If `≤ 0`: user gets ZERO allowance for that window; debt decreases by the renewed amount.
  - Any window renewing (Daily, Weekly, or Monthly) repays the single debt proportionally.
  - Only when `outstandingDebt == 0` does renewed allowance become usable; Daily/Weekly become usable again once debt is fully repaid.
- **Critical rule:** as long as `OutstandingDebt > 0`, the user may consume **no** renewed allowance; renewals repay debt first.

---

## 2. What ADR-018 actually says (re-read)

ADR-018 is **mostly consistent** with the business rule — but contains one internal inconsistency that the code amplified.

| ADR-018 location | Stated model | Consistent with business rule? |
|------------------|--------------|--------------------------------|
| §2 Ubiquitous Language — "Debt" | "excess consumption … a **separate business liability**. Survives rollover." | ✅ Single liability |
| §3.5 Debt | "recorded as **Debt** … a **separate ledger** … survives period rollover" | ✅ Single ledger |
| Invariant 4 | "excess is recorded as **debt**" | ✅ |
| Invariant 5 | "**OutstandingDebt**" (singular) | ✅ |
| Invariant 6 | "`Available = RenewedAllowance − **OutstandingDebt**`" | ✅ Single debt |
| Invariant 7 | "If **monthly debt** exists, Daily/Weekly blocked" | ✅ This is the *dominance* shorthand: "monthly debt" = "any outstanding debt" in the business rule. Consistent. |
| §7 Agg C (`DebtLedger`) | "`DebtPosition` (outstanding debt) **and** `RecoveryPosition`" — "These are **separate, named concepts**" | ✅ Debt is a single position |
| §20 Pipeline, **stage 7** | "On exceed, `DebtLedger.IncurDebt(excess)`; **cascade Daily→Weekly→Monthly**" | ❌ **Inconsistent** — implies per-window debt cascade |
| §8/§11 narrative | "`RecoveryProcessor` — on `WindowRolledOver`, renews allowance and applies **debt-first recovery**" | ⚠️ Ambiguous — "WindowRolledOver" (singular/any) vs the stage-7 cascade |

**Finding D1 — ADR-018 §20 stage 7 is internally inconsistent with its own invariants (4–7) and with the business rule.** It says "cascade Daily→Weekly→Monthly", which encodes *per-window* debt. The invariants (single `OutstandingDebt`) and the business rule (single liability, repaid by any renewal) contradict that.

**Finding D2 — The current code implements the WRONG (per-window) interpretation**, not the ADR's invariant-correct one:
- `DebtLedger.Debts` is `List<DebtEntry>` keyed by **(action, window)** (`DebtLedger.cs:18`, `DebtEntry.cs`). Debt is stored per window.
- `IncurDebt(action, window, excess)` writes the excess into that specific `(action, window)` bucket (`DebtLedger.cs:38-58`).
- `Recover(action, window, renewedAllowance)` recovers **only that window's** bucket (`DebtLedger.cs:60-81`).
- `RecoveryProcessor.Recover` hard-codes `QuotaWindow.Monthly` (`RecoveryProcessor.cs:11`).
- `RecoverDebtCommandHandler` resolves **only** `quota.Value!.Quota.Monthly.Value` (`RecoverDebt.cs:62`) and checks `OutstandingDebt(action, Monthly)` (`RecoverDebt.cs:47`).
- `AllowanceEngine.Consume` incurs debt **per window** in a loop and gates only on `MonthlyDebt` (`AllowanceEngine.cs:36-58`).
- `RecoveryProcessorJob` iterates `ledger.Debts.Where(d => d.Amount.Value > 0)` and sends one `RecoverDebtCommand` **per (action, window) debt** (`RecoveryProcessorJob.cs:84-92`) — so Daily/Weekly debt buckets are dispatched but the handler ignores them (Monthly-only), leaving them **unrecovered forever**.

So the code's per-window model violates:
- The business rule (debt must be single, repaid by any renewal).
- ADR-018 invariants 4–7 (single `OutstandingDebt`, `Available = renewed − outstandingDebt`, any-renewal repays).
- ADR-018 §7 ("`DebtPosition` … a separate concept" — singular debt position, not a per-window map).

---

## 3. Required model correction

### 3.1 Debt is a single outstanding liability per (consumer, action)
- `DebtLedger` holds **one** `DebtAmount` (outstanding) per **(consumer, action)** — **not** per (action, window). The window dimension is removed from debt.
- Rationale: the business rule explicitly says "Debt is NOT attached to Daily, Weekly or Monthly windows." Debt is denominated in allowance units and is the same pool regardless of which window's consumption created it or which window's renewal repays it.
- Retain `ActionKey` on debt because the example treats each operation/action independently (a 460-search debt is tied to the search action, not to "all consumption"). This preserves the existing per-action partitioning, which is **not** in conflict with the business rule (the rule is silent on action; it only forbids per-*window* debt). If the business later wants a single consumer-wide debt, `ActionKey` can be dropped — flag as open question OQ-1.

### 3.2 Usage remains per (action, window) — unchanged
- `UsageLedger` stays per (action, window) (tallies reset on each window's rollover). This is correct: usage measures *how much of each window's allowance was consumed*. The excess over the window limit becomes debt, but debt is then **consolidated into the single (action) liability**, not kept per window.
- `AllowanceEngine.Consume` still records usage per window and computes the per-window excess, but **all excess for an action is added to the single (action) debt** (see §3.4).

### 3.3 Recovery is per renewal event, repaying the single debt
- A renewal event carries a `renewedAllowance` for **one window** (Daily=20, Weekly=100, Monthly=300). On that event:
  - `repaid = min(renewedAllowance, outstandingDebt)`
  - `outstandingDebt -= repaid`
  - `availableForWindow = renewedAllowance − repaid`  → this window's usable allowance this period.
- **Any** window renewal repays the same single debt. Daily renewals chip away daily; a Monthly renewal can clear it in one shot (example: debt 160, Monthly renews 300 → debt 0, Monthly available 140, and Daily/Weekly become usable again).
- `RecoveryPosition` (remaining usable allowance for the window that just renewed) is kept **per (action, window)** because it is the *window's* post-repayment allowance, distinct from the debt. This matches ADR-018 §7 ("`RecoveryPosition` … a separate concept") and the business rule's per-window `availableAllowance`.

### 3.4 Invariants — corrected list (supersedes ADR-018 §8 items touched)
- I4 (unchanged): Excess becomes **single (action) debt**.
- I5 (rewritten): **`OutstandingDebt` is single per (consumer, action)**; survives rollover.
- I6 (rewritten): On **any** window renewal: `availableAllowance(window) = renewedAllowance(window) − outstandingDebt`; if `≤ 0`, window allowance = 0 and `outstandingDebt −= renewedAllowance`.
- I7 (rewritten): As long as `OutstandingDebt(action) > 0`, **no** renewed allowance for **any** window is usable for that action; renewals repay debt first. (The "monthly debt dominates" phrasing is retained as the common case but generalized to "any outstanding debt.")
- I2/I3/I8/I9/I10 unchanged.

### 3.5 Aggregate responsibilities (unchanged boundaries, corrected internals)
- `DebtLedger`: `OutstandingDebt` becomes a single `DebtAmount` per action (not a per-window list). `Recover(renewedAllowance per window)` repays the single debt and records the window's `RecoveryPosition`. `IncurDebt(totalExcessForAction)` adds to the single debt. `Reset()` clears it.
- `UsageLedger`: unchanged (per-window tallies).
- `AllowanceEngine`: `Consume` computes per-window excess, **sums the excess across windows for the action** into one `IncurDebt(totalExcess)`, and gates on `OutstandingDebt(action) > 0` (not just Monthly).
- `RecoveryProcessor`: on a renewal event for window W with `renewedAllowance(W)`, repay the single (action) debt and set `RecoveryPosition(W)`.
- `RecoveryProcessorJob`: on each run, for each (consumer, action) with `OutstandingDebt > 0`, determine which windows **renewed since the last processed time** and dispatch a **per-renewal-event** recovery (one command per renewal event, carrying the window + its renewed allowance), not one command per (action, window) debt bucket.

---

## 4. ADR amendment required (D1)

Amend **ADR-018 §20, stage 7** and the §8/§11 ambiguity so the ADR is internally consistent and matches the business rule:

- §20 stage 7 → "On exceed, `DebtLedger.IncurDebt(totalExcessForAction)` records the **single (action) debt** (NOT per-window). Debt is a single outstanding liability; windows only determine when renewed allowance appears."
- §20 add stage between 7 and 8 (Recovery): "On **any** window `WindowRolledOver(W)` with `renewedAllowance(W)`: `repaid = min(renewedAllowance(W), OutstandingDebt)`; `OutstandingDebt −= repaid`; `RecoveryPosition(W) = renewedAllowance(W) − repaid`. If `OutstandingDebt > 0`, `RecoveryPosition(W) = 0` (no usable allowance that window)."
- §11 `RecoveryProcessor` description → "on **each** `WindowRolledOver` event (Daily/Weekly/Monthly), repays the single outstanding debt; not a monthly-only process."
- §7 `DebtLedger` → clarify "`DebtPosition` is **single per (consumer, action)**; the window axis lives only in `UsageLedger` (tally) and `RecoveryPosition` (per-window post-repayment allowance)."
- Invariants 5/6/7 → rewrite per §3.4 above.
- Add explicit statement: "**Debt is never stored per window.** The per-window dimension exists only in usage tallies and in the per-window recovery allowance (`RecoveryPosition`)."

The ADR's core (§2, §3.5, invariants 4–6) already matches the business rule; only the pipeline-stage-7 "cascade" wording and the Monthly-only recovery implication must be corrected. **No new aggregate is introduced; the existing `UsageLedger`/`DebtLedger` boundary is retained** (consistent with ADR-015/SharedKernel/Platform conventions — no transaction-boundary change).

---

## 5. Execution pipeline (corrected) — ADR-018 §20 amendment

```
1 Subscription Resolution → 2 Policy Resolution → 3 Quota Resolution
        ↓
4 Debt Resolution        (load DebtLedger.OutstandingDebt(action) — SINGLE)
        ↓
5 Consumption Decision    (always succeeds; never truncate;
                           if OutstandingDebt(action) > 0 → no usable allowance,
                           but operation still returns full result)
        ↓
6 Usage Recording         (per window tally; reset on window rollover)
        ↓
7 Debt Update             (IncurDebt(totalExcessForAction) — SINGLE (action) debt,
                           NOT per-window cascade)
        ↓
   ... Outbox / Audit / OPA Sync (unchanged, ADR-015/017/003)
```

**Recovery (separate trigger, per renewal event):**
```
WindowRolledOver(W, renewedAllowance(W))
        ↓
DebtLedger.Recover(W, renewedAllowance(W))
   repaid = min(renewedAllowance(W), OutstandingDebt)
   OutstandingDebt -= repaid
   RecoveryPosition(W) = renewedAllowance(W) - repaid   (0 if debt remains)
        ↓
DebtRecovered event (amount repaid, remaining debt, window W)
```

---

## 6. How `RecoveryProcessor` should work after correction

`RecoveryProcessor` is the domain service that applies one renewal event to the single debt. It must be **window-agnostic about the debt** and **window-specific about the allowance**:

```text
RecoveryProcessor.Recover(debtLedger, actionKey, window, renewedAllowance, correlationId):
    debt = debtLedger.OutstandingDebt(actionKey)          // SINGLE, no window arg
    if debt <= 0:
        debtLedger.SetRecovery(window, renewedAllowance) // fully usable
        return
    repaid      = min(renewedAllowance, debt)
    remaining   = debt - repaid
    debtLedger.SetOutstandingDebt(actionKey, remaining)  // single debt reduced
    debtLedger.SetRecovery(window, renewedAllowance - repaid)  // this window's usable allowance
    raise DebtRecovered(actionKey, window, repaid, remaining)
```

Key differences from today:
- It takes a **window + that window's renewed allowance** (so Daily/Weekly/Monthly renewals all repay the same debt).
- It reads/writes the **single** `(action)` debt, not a `(action, window)` bucket.
- It sets `RecoveryPosition` **per window** (the post-repayment usable allowance for that window).
- It does **not** special-case Monthly.

`RecoveryProcessorJob` drives it: instead of enumerating `(action, window)` debt buckets, it must detect **window rollovers that occurred since the last scan** per (consumer, action) and dispatch a recovery **per rollover event** (window + renewed allowance). Mechanism options (pick one at implementation):
- **(a) Lazy first-access:** when `AllowanceEngine.Consume` runs and detects a window boundary has passed (via `UsageLedger` window markers), trigger `RecoveryProcessor.Recover` for the elapsed windows before applying the operation. Simplest, no scheduler dependency, naturally "per renewal event". (Recommended.)
- **(b) Scheduled sweep:** `RecoveryProcessorJob` keeps a `lastProcessedUtc` watermark per (consumer, action) and, each 5-min tick, computes which of Daily/Weekly/Monthly boundaries fell in `(lastProcessedUtc, now)` and dispatches recovery for each. More moving parts but explicit.
- Either way, the **renewed allowance** for the window comes from the resolved effective `QuotaPolicy` (already resolved in `RecoverDebtCommandHandler`); the job/handler must resolve quota **per window** and pass `renewedAllowance(window)`, not just `Monthly`.

The dominant-gate (`OutstandingDebt(action) > 0 ⇒ no usable allowance`) is enforced in `AllowanceEngine.Consume` (check the single debt) and reflected in `RecoveryPosition` (0 while debt remains). This makes "Daily/Weekly become usable again once debt fully repaid" fall out automatically.

---

## 7. Aggregate / VO / spec changes required (no new aggregates)

| Artifact | Change | ADR-compliant? |
|----------|--------|----------------|
| `DebtLedger.Debts` (`List<DebtEntry>` per (action,window)) | Replace with single `Dictionary<ActionKey, DebtAmount>` (or `OutstandingDebts` map) — **no window axis** | ✅ §7 |
| `DebtEntry` (carries `Window`) | Remove `Window`; debt is `(action) → amount` only. `RecoveryEntry` keeps `Window` (per-window allowance) | ✅ §7 |
| `DebtLedger.IncurDebt(action, window, excess)` | → `IncurDebt(action, totalExcessAcrossWindows)`; adds to single (action) debt | ✅ I4/I5 |
| `DebtLedger.Recover(action, window, renewed)` | → repay **single** debt by `renewed` (window only selects allowance + sets `RecoveryPosition(window)`) | ✅ I6/I7 |
| `DebtLedger.OutstandingDebt(action, window)` | → `OutstandingDebt(action)` (single) | ✅ I5 |
| `DebtLedger.MonthlyDebt(action)` | → `OutstandingDebt(action)` (dominance uses single debt) | ✅ I7 |
| `AllowanceEngine.Consume` | Sum per-window excess → one `IncurDebt`; gate on `OutstandingDebt(action) > 0` | ✅ I3/I6/I7 |
| `RecoveryProcessor.Recover` | Window-agnostic debt, window-specific allowance (per §6) | ✅ |
| `RecoverDebtCommand` `(ConsumerId, ActionKey)` | Add `Window` + `RenewedAllowance` (per renewal event) OR switch to lazy first-access (§6) | ✅ ADR-015 (still `ITransactionalRequest`) |
| `RecoveryProcessorJob` | Detect rollovers since last watermark; dispatch **per renewal event**, not per (action,window) bucket | ✅ ADR-018 §9 (per-consumer boundary kept) |
| `RecoveryEligibilitySpecification` | `IsEligible(OutstandingDebt(action))` (already single — OK; just stop passing `Monthly`) | ✅ |
| `DebtDominanceSpecification` | `IsDominant(OutstandingDebt(action))` (already single — OK) | ✅ |
| `DebtLedgerConfiguration` / `DebtEntry` table | Drop `window` column from `debt_entries`; `recovery_entries` keeps `window` | ✅ (migration needed) |
| Events `DebtIncurred`/`DebtRecovered`/`DebtReset` | `DebtIncurred` drops `Window`; `DebtRecovered` adds `Window` (the renewal that repaid) + `RemainingDebt` | ✅ ADR-012 (TenantId+CorrelationId only) |

**Transaction boundary:** unchanged — `AllowanceEngine` still mutates `UsageLedger`+`DebtLedger` in one `UnitOfWorkBehavior` transaction (ADR-015). `RecoveryProcessor` runs inside the same per-consumer transaction via the recovery command. No cross-aggregate or cross-service transaction change. ✅

**Tenant isolation:** unchanged — `DebtLedger`/`UsageLedger` are `Entity`-derived, tenant-filtered; recovery command runs under the debtor's `RequestContext` (already done in `RecoveryProcessorJob`). ✅ (ADR-013/014)

**Outbox/events:** debt events are domain events → captured in `OutboxMessage` atomically (already correct, pending B4 fix). Event payloads carry `TenantId`+`CorrelationId` only (ADR-012). ✅ (ADR-017)

---

## 8. Open questions to confirm with the business (do not block design, but record)

- **OQ-1 — Debt scope:** single `(consumer)` debt vs `(consumer, action)` debt. Business example ties 160 to the search action; I model `(consumer, action)`. Confirm whether debt should ever be consumer-wide. (Current code is per-(action,window); moving to per-(action) is the minimal correct step; per-consumer is a further simplification if desired.)
- **OQ-2 — Multiple actions:** if a consumer has debt on action A (search) and also consumes action B (export) with no debt, does B's renewal repay A's debt? Per the business rule ("renewals repay the single outstanding debt"), **yes** — any renewal repays the single (action) debt. But with per-(action) debt, B's renewal repays only B's debt. **This is the key semantic fork:** the business rule says debt is *single* (consumer-wide); the code structure is per-action. I recommend per-(action) as the pragmatic interpretation (each action's excess is its own liability), but if the business intends a single consumer-wide debt, drop `ActionKey` from `DebtLedger`. Flag for sign-off.
- **OQ-3 — Rollover trigger:** scheduler (`RecoveryProcessorJob`) vs lazy first-access. Recommend lazy first-access (§6a) — it is naturally "per renewal event" and removes the job's enumeration complexity. Confirm.
- **OQ-4 — Renewed allowance source:** confirmed from resolved effective `QuotaPolicy` per window (already done); ensure `RecoverDebtCommandHandler` resolves **per window**, not just `Monthly.Value`.
- **OQ-5 — `RecoveryPosition` semantics:** kept per (action, window) as the post-repayment usable allowance for that window. Confirm this is the intended "available allowance" the business rule refers to (it is per-window by definition).

---

## 9. Implementation TODO list (after architecture approval)

1. **ADR-018 amendment** — correct §20 stage 7, add recovery stage, fix invariants 5/6/7, clarify §7/§11 (no per-window debt). (D1)
2. **Migration** — `debt_entries`: drop `window` column; PK → `(DebtLedgerId, action)`. `recovery_entries`: keep `window`. (New migration; coordinate with B3/B4.) Backfill: sum existing per-(action,window) debt into single (action) `OutstandingDebt`.
3. **`DebtEntry`** — remove `Window`; debt = `(action) → amount`.
4. **`DebtLedger`** — replace `List<DebtEntry>` with single outstanding-debt map; rewrite `IncurDebt(action, totalExcess)`, `Recover(action, window, renewedAllowance)`, `OutstandingDebt(action)`, `MonthlyDebt(action)→OutstandingDebt(action)`, `Reset()`.
5. **`AllowanceEngine.Consume`** — sum per-window excess → one `IncurDebt`; gate on `OutstandingDebt(action) > 0`.
6. **`RecoveryProcessor.Recover`** — repay single debt; set `RecoveryPosition(window)` (per §6).
7. **Recovery trigger** — implement lazy first-access (preferred) or watermarked `RecoveryProcessorJob`; dispatch **per renewal event** with `(window, renewedAllowance(window))`; remove the per-(action,window) bucket loop.
8. **`RecoverDebtCommand`** — add `Window`+`RenewedAllowance` (or remove if lazy); resolve quota **per window** in handler.
9. **Events** — `DebtIncurred` drop `Window`; `DebtRecovered` add `Window`+`RemainingDebt`; verify consumers/audit unaffected (ADR-017/012).
10. **Specs** — `RecoveryEligibilitySpecification`/`DebtDominanceSpecification` take single `OutstandingDebt(action)` (already do; update call sites to stop passing `Monthly`).
11. **Tests** — executable specs for: (a) 460 over 300 → debt 160 single; (b) Daily renew 20 → debt 140, Daily available 0; (c) after 8 days debt 0, Daily available; (d) Monthly renew 300 → debt 0, Monthly available 140, Daily/Weekly usable; (e) while debt>0 no window usable.
12. **Docs** — update domain-discovery docs (`docs/domain-discovery/*`) debt/recovery sections to single-liability model; update `01-remediation-plan.md` M2 to reference this design (replace the per-window M2 fix with the single-debt correction).
13. **Verify compliance** — re-confirm ADR-012/013/014/015/017/018 adherence; build green; end-to-end smoke (incur → recover across Daily/Weekly/Monthly renewals).

---

## 10. Compliance statement

After this correction, the model remains fully compliant with:
- **ADR-012** — events carry `TenantId`+`CorrelationId` only; no `RequestContext` in commands/events. ✅
- **ADR-013** — tenant resolved from JWT/event, propagated only; RLS + global filter unchanged. ✅
- **ADR-014** — PolicyService references principals by id; no department ownership. ✅
- **ADR-015** — single `UnitOfWorkBehavior` transaction; no new transaction owners; outbox atomic. ✅
- **ADR-017** — domain events → outbox; consumers idempotent; no dual MassTransit registration. ✅
- **ADR-018** — debt as single liability, recovery per renewal event, never truncate, dominance, reset-clears-usage+debt (now correct after §4 amendment). ✅

No new aggregates, no transaction-boundary change, no Platform/SharedKernel convention violation. The change is a **correction of the debt/recovery internals** to match the authoritative business rule.

> No code was modified by this review document itself. The design is now **approved** and the
> codebase has since been corrected to match: debt is stored per (Consumer, Action) with no window
> axis (`DebtEntry` has no `Window`), `RecoveryPosition` is persisted per (Consumer, Action, Window),
> recovery is **Lazy First-Access** inside `AllowanceEngine.Consume` (the `RecoveryProcessorJob`
> scheduler is removed), and ADR-018 §3.6/§10/§11/§16 have been amended accordingly. Schema
> synchronization is verified in `03-schema-verification.md`.

---

## 11. Architecture Decisions (Approved 2026-07-16)

The following decisions are ratified as the canonical implementation model and supersede any
earlier documentation describing scheduled recovery or per-window debt handling:

1. **Debt Scope — per Action (Option A).** Outstanding debt is maintained **per (Consumer, Action)**.
   Each action owns an independent debt position; renewals for that action repay only that action's
   debt. The debt model MUST NEVER include a quota-window dimension.
2. **`RecoveryPosition` — persisted domain state.** `RecoveryPosition` represents the remaining
   usable allowance for a window after debt-first recovery. It is **persisted per (Consumer, Action,
   Window)** and is part of the aggregate's business state (not a transient calculation).
3. **Window Renewal Detection — Lazy Recovery (First Access After Renewal).** When the first request
   arrives after a Daily/Weekly/Monthly boundary, the system (a) detects every elapsed renewal event,
   (b) applies debt-first recovery for each renewed window, (c) persists `OutstandingDebt` +
   `RecoveryPosition`, (d) continues normal quota evaluation. Recovery is triggered by first access,
   not by background processing.
4. **Scheduler — Removed.** The scheduled `RecoveryProcessorJob` is **removed** from the architecture
   (unnecessary infrastructure complexity, watermark management, timing concerns, operational
   overhead, no business benefit over Lazy Recovery). The ADR describes the scheduler only as a
   historical / alternative approach, never as the canonical design.
5. **ADR-018 updates (applied).** Debt per (Consumer, Action); never per window; `RecoveryPosition`
   persisted per (Consumer, Action, Window); Lazy Recovery; renewal detection during request
   processing; `RecoveryProcessorJob` removed from reference architecture; all recovery examples,
   sequence diagrams, invariants, and the execution pipeline updated to the Lazy Recovery model.
