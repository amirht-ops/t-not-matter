# Inconsistency Report — Approved ADR-017 vs. Current Codebase

**Status:** IMPLEMENTATION HALTED — Phase 1 (MSG-001 / MSG-002 / MSG-003 / MSG-004) NOT started.
**Date:** 2026-07-14
**Author:** Principal Software Engineer (implementation phase)
**Trigger:** Implementation rule — "If documents disagree: STOP. Do not guess. Produce an inconsistency report." / "If implementation already differs from documentation, stop and explain why."

---

## 1. Problem

The approved ADR **`docs/adr/ADR-017-messaging-architecture-audit.md`** (dated 2026-07-09, Status: approved baseline) describes a RabbitMQ/MassTransit messaging topology that **materially contradicts the current source code** in three independent, verifiable ways. The implementation source-of-truth hierarchy places approved ADRs first, so building the approved Messaging TODO against this ADR would implement work that the ADR already claims is done — while the code shows it is not.

The divergence is severe enough that the "baseline architecture" cannot be trusted as the single source of truth without reconciliation.

---

## 2. Evidence

### Conflict A — IdentityService "dual MassTransit registration" / explicit queues
- **ADR-017 claims** (lines 40-44, 98-101, 510-511): IdentityService registers MassTransit **twice** — once in `IdentityService.Infrastructure\DependencyInjection.cs` (lines 93-132) with explicit queues `identity.session.revocation` and `identity.tenant.cache`, and again in `IdentityService.Api\Extensions\ServiceCollectionExtensions.cs` with auto-generated names. The consumer `TenantStatusChangedConsumer` is said to subscribe to **two** queues (`identity.session.revocation` + auto-generated).
- **Actual code:**
  - `src/Services/IdentityService/IdentityService.Infrastructure/DependencyInjection.cs` — **FILE DOES NOT EXIST** (verified: read returned "File not found").
  - Grep for `AddMassTransit` across the entire `src` tree returns exactly **3** registrations:
    - `TenantService.Infrastructure\DependencyInjection.cs:103`
    - `AuthorizationService.Api\Extensions\ServiceCollectionExtensions.cs:61`
    - `IdentityService.Api\Extensions\ServiceCollectionExtensions.cs:135`
  - There is **no** second registration in IdentityService, and **no** `identity.session.revocation` / `identity.tenant.cache` queue definitions anywhere (grep for those strings → 0 matches).

### Conflict B — IdentityService quorum queues + RabbitMQ DLQ
- **ADR-017 claims** (lines 36, 229-234, 526-528): "All queues use quorum queues"; IdentityService has `identity.session.revocation.dlq` / `identity.tenant.cache.dlq` bound to `.dlx` exchanges.
- **Actual code:** `SetQuorumQueue()` / `BindDeadLetterQueue` appear **only** in:
  - `TenantService.Infrastructure\DependencyInjection.cs:131-132` (`tenant.cache`)
  - `AuthorizationService.Api\Extensions\ServiceCollectionExtensions.cs:96-121` (5 queues)
  - **Zero** occurrences in IdentityService. The single IdentityService `AddMassTransit` (`ServiceCollectionExtensions.cs:135-160`) uses `ConfigureEndpoints(context)` with **no** `UseMessageRetry`, **no** `SetQuorumQueue`, **no** `BindDeadLetterQueue`. (This matches the earlier `RABBITMQ-ARCHITECTURE-REPORT.md` finding, High risk #2.)

### Conflict C — `DepartmentSoftDeletedV1` "produced by TenantService"
- **ADR-017 claims** (lines 106, 517, 549): `DepartmentSoftDeletedV1` is **Produced By: TenantService** and **Consumed By: `DepartmentSoftDeleteConsumer`**. The routing-key inventory lists it as an existing, wired event.
- **Actual code:** Grep for `DepartmentSoftDeleted` across the **entire** repository returns **only**:
  - `AuthorizationService.Infrastructure/Messaging/Consumers/DepartmentSoftDeleteConsumer.cs:18,24`
  - **No producer exists in any service.** TenantService's `DeleteDepartmentCommandHandler` raises `DepartmentStatusChangedV1` (`src/Services/TenantService/TenantService.Application/Features/DeleteDepartment/DeleteDepartmentCommandHandler.cs:25`), and `TenantDomainEvents.cs` contains **no** `DepartmentSoftDeleted` type. (This matches `RABBITMQ-ARCHITECTURE-REPORT.md` Critical risk #1.)

### Note on consistency with the discovery report
The `RABBITMQ-ARCHITECTURE-REPORT.md` (produced by reading the actual files) agrees with the **code**, not with ADR-017, on all three points. So the two approved documents (ADR-017 vs. RABBITMQ report) **disagree with each other**, and the code agrees with the report.

---

## 3. Impact

1. **MSG-001 / MSG-002 (publish `DepartmentSoftDeletedV1` from TenantService).** ADR-017 asserts this event already exists and is wired. If I implement the TODO, I am "adding" something the highest-priority baseline says is already present — yet the code proves it is absent. Proceeding would be implementing against a false premise and could not be claimed as "faithful to the approved architecture."
2. **MSG-003 (add retry/DLQ to IdentityService).** ADR-017 asserts IdentityService already has quorum + DLQ + dual registration. If I implement MSG-003, I would be adding what the baseline claims exists. The real defect (no retry/DLQ on IdentityService) is real per code, but the baseline documentation denies it — so the TODO's premise is in conflict with the ADR.
3. **MSG-004 (remove dead switch-cases `UserLoggedInV1` / `MfaVerifiedV1`).** ADR-017's consumer matrix (lines 102, 551, 558) lists `UserLoggedInV1` and `MfaVerifiedV1` as events "Produced By: IdentityService." The code shows neither is produced. ADR-017 and code disagree on whether these are dead — confirming the conflict is systemic, not isolated.
4. **Trust in the baseline.** Because the top-priority source of truth (ADR-017) does not match the code, **none** of the four TODO items can be implemented "faithfully to the approved architecture" without first establishing which artifact is authoritative. Any implementation I produce could later be invalidated by the "correct" baseline.

---

## 4. Recommendation

1. **Do NOT implement Phase 1 yet.** Halt MSG-001..MSG-004 until the baseline is reconciled.
2. **Establish the authoritative baseline.** Determine whether ADR-017 describes:
   - (a) a *target/planned* future state that was never merged, or
   - (b) a *stale* audit of a previous branch/commit, or
   - (c) the intended end-state that should supersede the current code.
   The most likely reality (supported by the code agreeing with `RABBITMQ-ARCHITECTURE-REPORT.md`) is that ADR-017 is **stale or aspirational** and the current code + RABBITMQ report reflect the actual as-built system.
3. **If ADR-017 is stale/aspirational:** reclassify it (e.g., "Proposed" or "Target State") and confirm `RABBITMQ-ARCHITECTURE-REPORT.md` + the Messaging Implementation Plan/TODO as the operative baseline. Then Phase 1 can proceed as planned (the TODO items remain valid against the real code).
4. **If ADR-017 is the intended end-state:** the gap is far larger than the 4 TODO items — it would require introducing the missing `IdentityService.Infrastructure\DependencyInjection.cs`, the dual-registration topology, the `identity.*` queues, and the `DepartmentSoftDeletedV1` producer. In that case the current TODO is **incomplete** and must be expanded before any code is written.
5. **Resolve before coding.** This is a documentation/authority conflict, not an implementation bug. No source code should change until the authoritative baseline is confirmed.

---

## 5. What was NOT done

- No files were modified.
- No TODO item (MSG-001..MSG-004) was implemented.
- No new abstractions, infrastructure, or architectural changes were introduced.
- No baseline documents (`MESSAGING-IMPLEMENTATION-PLAN.md`, `MESSAGING-TODO.md`, `MESSAGING-ROADMAP.md`) were written to disk, because writing them on top of an inconsistent ADR baseline would propagate the inconsistency.

---

## 6. Suggested next step (await decision)

Confirm which artifact is authoritative:
- **Option 1 (recommended):** `RABBITMQ-ARCHITECTURE-REPORT.md` + Messaging Implementation Plan/TODO are authoritative; ADR-017 is stale. Proceed with Phase 1 as planned.
- **Option 2:** ADR-017 is authoritative/target state; expand the TODO to cover the missing infrastructure first, then implement.

I will resume implementation only after this is resolved.
