# Phase 1 Messaging Defect Remediation — Implementation Report

- **Date:** 2026-07-14
- **Author:** Principal Software Engineer (implementation phase)
- **Scope:** Approved scope = Immediate + Future. This report covers the full approved TODO set: `MSG-001`, `MSG-002`, `MSG-003`, `MSG-004`.
- **Authoritative baseline:** `docs/architecture/RABBITMQ-ARCHITECTURE-REPORT.md`, `docs/architecture/MESSAGING-IMPLEMENTATION-PLAN.md`, `docs/todo/MESSAGING-TODO.md`, `docs/architecture/MESSAGING-ROADMAP.md`. `ADR-017-messaging-architecture-audit.md` is **stale** (see `docs/reports/INCONSISTENCY-REPORT-ADR017.md`) and was **not** used as a source of truth.

## Summary

All four approved messaging defect items are implemented and the solution builds with **0 errors** (warnings are pre-existing and unrelated). A new focused unit test project validates the critical dead-event fix.

| ID | Risk | Item | Status |
|----|------|------|--------|
| MSG-001 | Critical | Add `DepartmentSoftDeletedV1` event | Done |
| MSG-002 | Critical | Raise it from `DeleteDepartmentCommandHandler` | Done |
| MSG-003 | High | IdentityService consumer retry / quorum / DLQ | Done |
| MSG-004 | Medium | Remove dead `UserLoggedInV1` / `MfaVerifiedV1` switch cases | Done |

## MSG-001 / MSG-002 — Critical: dead `DepartmentSoftDeleteConsumer`

**Root cause (per RABBITMQ report, Risk #1):** `AuthorizationService` consumes `DepartmentSoftDeletedV1`, but no producer existed anywhere in the codebase, so role-disable-on-tenant-delete never fired.

**Changes:**
- `src/Services/TenantService/TenantService.Domain/Events/TenantDomainEvents.cs`
  - Added `DepartmentSoftDeletedEvent` (record) with `EventTypeName => "DepartmentSoftDeletedV1"`, carrying `TenantId`, `DepartmentId`, `Name`, `CorrelationId`. Mirrors neighbouring event records (`DepartmentDescriptionUpdatedEvent`, etc.).
- `src/Services/TenantService/TenantService.Domain/Aggregates/Department.cs`
  - Added `SoftDelete(Guid correlationId)` which raises `DepartmentSoftDeletedEvent`. Preserves existing `Deactivate`/`DepartmentStatusChangedV1` behaviour — the two events are independent and both raised on delete.
- `src/Services/TenantService/TenantService.Application/Features/DeleteDepartment/DeleteDepartmentCommandHandler.cs`
  - After `department.Deactivate(correlationId)` (which already raises `DepartmentStatusChangedV1`), calls `department.SoftDelete(correlationId)`. The status event is intentionally retained; the consumer contract for `DepartmentSoftDeletedV1` remains the sole trigger for role disablement.

**Verification:** unit test `DepartmentSoftDeleteTests` asserts (a) the event is raised with `EventTypeName == "DepartmentSoftDeletedV1"`, correct `DepartmentId`/`TenantId`/`Name`, and (b) both `DepartmentStatusChangedEvent` and `DepartmentSoftDeletedEvent` are present. 2/2 pass.

## MSG-003 — High: IdentityService lacks retry / quorum / DLQ

**Root cause (Risk #2):** `TenantService` and `AuthorizationService` configure `UseMessageRetry` (exponential), `SetQuorumQueue()`, and `BindDeadLetterQueue(...)` on their receive endpoints. `IdentityService.Api` used `cfg.ConfigureEndpoints(context)` with none of these, so its consumers (`TenantStatusChangedConsumer`, `TenantCreatedCacheInvalidationConsumer`, `AuthorizationRoleAssignedConsumer`) had no retry or dead-letter protection.

**Change:** `src/Services/IdentityService/IdentityService.Api/Extensions/ServiceCollectionExtensions.cs`
- Replaced the bare `ConfigureEndpoints(context)` with the same hardening pattern used by the other two services:
  - `cfg.UseMessageRetry(r => r.Exponential(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)))`
  - Explicit `cfg.ReceiveEndpoint(...)` per consumer, each with `SetQuorumQueue()` and `BindDeadLetterQueue("<queue>.dlx", "<queue>.dlq")`.
  - Queue names: `identity.tenant-status-changed`, `identity.tenant-created-cache-invalidation`, `identity.authorization-role-assigned` (prefixed `identity.` to avoid collision with sibling service queues).
- Removed the now-unused `SetKebabCaseEndpointNameFormatter()` call (it had no effect once endpoints are explicitly named).

## MSG-004 — Medium: dead switch cases in `AuthorizationCacheInvalidationConsumer`

**Root cause:** The consumer's `switch (envelope.EventType)` handled `UserLoggedInV1` and `MfaVerifiedV1`, but neither event is ever produced. `UserLoggedInV1` is defined only as an orphaned `UserLoggedInDomainEvent` that is never raised/published; `MfaVerifiedV1` has no producer at all (grep across the solution returned zero publisher matches).

**Change:** `src/Services/AuthorizationService/AuthorizationService.Infrastructure/Messaging/Consumers/AuthorizationCacheInvalidationConsumer.cs`
- Removed the `case "UserLoggedInV1":` and `case "MfaVerifiedV1":` labels from the user-event fall-through group. Other live user events (`UserRegisteredV1`, `UserActivatedV1`, `UserLockedV1`, `UserUnlockedV1`, `UserDisabledV1`, `UserDeletedV1`, `MfaEnabledV1`, `SessionRevokedV1`) are untouched.

**Note:** The orphaned `UserLoggedInDomainEvent` definition remains in `IdentityService.Domain` but is out of scope for MSG-004 (the TODO item covers only the consumer switch cases). No producer depends on it, so leaving it does not affect runtime behaviour.

## Verification

- `dotnet build Enterprise-Security-Platform.sln -c Debug` → **0 errors** (4 warnings, all pre-existing: `NU1903` SQLitePCLRaw advisory, `CS8981` migration class names, `CS8618` pre-existing nullable on aggregate constructors).
- `dotnet test tests/TenantService.Domain.Tests/TenantService.Domain.Tests.csproj -c Debug` → **2 passed, 0 failed**.
- New test project added to the solution (`tests/TenantService.Domain.Tests`).

## Out of scope (not changed)

- No new abstractions, infrastructure, or cross-service responsibility moves.
- No redesign of exchange/topology beyond the approved hardening pattern.
- `ADR-017` left as-is (documented stale in `INCONSISTENCY-REPORT-ADR017.md`).
