# Messaging Implementation Plan

> Scope: **only work approved for implementation now** (Immediate defect fixes). No ADR required.
> Source of truth confirmed: `RABBITMQ-ARCHITECTURE-REPORT.md` + this plan + `MESSAGING-TODO.md`
> (ADR-017 is stale and superseded by the as-built report — see `docs/reports/INCONSISTENCY-REPORT-ADR017.md`).

## Item 1 — Department deletion does not propagate to AuthorizationService
- **Problem:** Deleting a department in TenantService never disables that department's roles in AuthorizationService. The `DepartmentSoftDeleteConsumer` never fires.
- **Root Cause:** `DepartmentSoftDeleteConsumer` matches `DepartmentSoftDeletedV1` (`AuthorizationService.../Consumers/DepartmentSoftDeleteConsumer.cs:18,24`), but `DeleteDepartmentCommandHandler` raises `DepartmentStatusChangedV1` (`TenantService.../Features/DeleteDepartment/DeleteDepartmentCommandHandler.cs:25`). A repo-wide grep finds `DepartmentSoftDeletedV1` referenced only by the consumer — **no producer exists**.
- **Proposed Fix:** In TenantService, add a `DepartmentSoftDeletedEvent` domain event with `EventTypeName = "DepartmentSoftDeletedV1"` carrying `DepartmentId`, and raise it when a department is deleted. The existing outbox + `DepartmentSoftDeleteConsumer` then disable the department's roles. (Decision (a) — new explicit event — approved.)
- **Affected Services:** TenantService (producer), AuthorizationService (consumer already present).
- **Affected Files:**
  - `src/Services/TenantService/TenantService.Domain/Events/TenantDomainEvents.cs` (add event)
  - `src/Services/TenantService/TenantService.Domain/Aggregates/Department.cs` (raise new event)
  - `src/Services/TenantService/TenantService.Application/Features/DeleteDepartment/DeleteDepartmentCommandHandler.cs` (call raise)
- **Dependencies:** None (architectural decision already approved in planning).
- **Acceptance Criteria:** Deleting a department publishes `DepartmentSoftDeletedV1`; AuthorizationService disables all active, non-disabled roles for that department (`RoleStatus.Disabled`). Existing `DepartmentStatusChangedV1` publication is preserved (department cache invalidation must keep working).
- **Required Tests:** Unit — `DeleteDepartmentCommandHandler` produces an outbox `OutboxMessage` with `EventType == "DepartmentSoftDeletedV1"` (or domain-level: `Department.SoftDelete` raises `DepartmentSoftDeletedEvent`). Integration — delete department → assert roles `Disabled` in AuthorizationService DB.
- **Priority:** Critical.

## Item 2 — IdentityService has no bus-level retry or RabbitMQ DLQ
- **Problem:** IdentityService consumer queues can silently lose messages on transient failures (e.g. Redis down) because MassTransit retry and RabbitMQ dead-letter are not configured, unlike the other two services.
- **Root Cause:** `IdentityService.Api/Extensions/ServiceCollectionExtensions.cs:135-160` uses `ConfigureEndpoints(context)` with **no** `UseMessageRetry`, **no** `SetQuorumQueue()`, **no** `BindDeadLetterQueue`.
- **Proposed Fix:** Add `cfg.UseMessageRetry(r => r.Exponential(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)))` and configure the three consumer endpoints (`authorization-role-assigned`, `tenant-created-cache-invalidation`, `tenant-status-changed`) with `SetQuorumQueue()` + `BindDeadLetterQueue("<queue>.dlx", "<queue>.dlq")`. (A shared bootstrap helper is a separate Roadmap item; here replicate the existing Authorization/Tenant pattern inline.)
- **Affected Services:** IdentityService.
- **Affected Files:** `src/Services/IdentityService/IdentityService.Api/Extensions/ServiceCollectionExtensions.cs`.
- **Dependencies:** None.
- **Acceptance Criteria:** IdentityService consumers retry on transient exceptions and route to a `.dlq` after exhaustion; configuration parity with AuthorizationService/TenantService.
- **Required Tests:** Integration — force transient consumer failure → message retried, then parked in the `.dlq` exchange/queue; no silent loss.
- **Priority:** High.

## Item 3 — Dead consumer switch-cases (`UserLoggedInV1`, `MfaVerifiedV1`)
- **Problem:** `AuthorizationCacheInvalidationConsumer` contains `case` labels for event types that are never produced, creating dead code and misleading intent.
- **Root Cause:** `AuthorizationCacheInvalidationConsumer.cs:36` (`UserLoggedInV1`) and `:43` (`MfaVerifiedV1`) reference events with no producer — `UserLoggedInDomainEvent` is declared (`IdentityDomainEvents.cs:25-28`) but never instantiated (Login emits `SessionCreatedV1`); `MfaVerifiedV1` has no domain event at all (`MfaVerifyCommandHandler` raises nothing).
- **Proposed Fix:** Remove the two dead `case` labels; keep `default` no-op. Confirm `UserLoggedInDomainEvent` has no other references and remove the declaration if unused.
- **Affected Services:** AuthorizationService (consumer); IdentityService (potential removal of unused domain event).
- **Affected Files:**
  - `src/Services/AuthorizationService/AuthorizationService.Infrastructure/Messaging/Consumers/AuthorizationCacheInvalidationConsumer.cs`
  - `src/Services/IdentityService/IdentityService.Domain/Events/IdentityDomainEvents.cs` (remove `UserLoggedInDomainEvent` only if no other usage)
- **Dependencies:** None.
- **Acceptance Criteria:** Consumer switch contains only event types that are actually produced; no dangling references remain.
- **Required Tests:** Static/unit test asserting every `case` in the consumer maps to an event type produced by some handler; grep confirms no orphaned references.
- **Priority:** Medium.
