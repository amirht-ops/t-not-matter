# Messaging Implementation TODO

Status legend: Pending | Done

> Implementation-ready TODO derived from `MESSAGING-IMPLEMENTATION-PLAN.md`. Future/architecture work is excluded (see `MESSAGING-ROADMAP.md`).

## Phase 1 — Critical correctness
### MSG-001: Publish DepartmentSoftDeletedV1 from TenantService
- ID: MSG-001
- Title: Add DepartmentSoftDeletedEvent domain event (EventType "DepartmentSoftDeletedV1")
- Priority: Critical
- Dependencies: None (decision already approved)
- Estimated Risk: Low
- Acceptance Criteria: New domain event exists with EventTypeName "DepartmentSoftDeletedV1" carrying DepartmentId; serialized correctly through TenantDbContext outbox.
- Validation Steps: Build + unit test that the event type is emitted by the aggregate/handler.
- Status: Done

### MSG-002: Raise department-soft-deleted on delete
- ID: MSG-002
- Title: DeleteDepartmentCommandHandler raises DepartmentSoftDeletedV1 (via Department.SoftDelete)
- Priority: Critical
- Dependencies: MSG-001
- Estimated Risk: Low
- Acceptance Criteria: Deleting a department publishes DepartmentSoftDeletedV1; AuthorizationService DepartmentSoftDeleteConsumer disables the department's roles; existing DepartmentStatusChangedV1 publication preserved.
- Validation Steps: Integration test — delete department -> roles Disabled in AuthorizationService DB; verify DepartmentStatusChangedV1 still published for other consumers.
- Status: Done

## Phase 2 — High reliability
### MSG-003: IdentityService bus retry + DLQ
- ID: MSG-003
- Title: Add UseMessageRetry + quorum + RabbitMQ DLQ to IdentityService consumers
- Priority: High
- Dependencies: None
- Estimated Risk: Low (config-only)
- Acceptance Criteria: IdentityService 3 queues use exponential retry(10) + quorum + .dlx/.dlq; parity with other services.
- Validation Steps: Force transient consumer failure -> message retried then parked in .dlq; no silent loss.
- Status: Done

## Phase 3 — Medium cleanup
### MSG-004: Remove dead switch-cases
- ID: MSG-004
- Title: Remove UserLoggedInV1 / MfaVerifiedV1 dead cases from AuthorizationCacheInvalidationConsumer
- Priority: Medium
- Dependencies: None
- Estimated Risk: Low
- Acceptance Criteria: Consumer switch contains only produced event types; UserLoggedInDomainEvent removed if unused.
- Validation Steps: Static test mapping cases->produced events; grep for orphaned references.
- Status: Done (UserLoggedInDomainEvent definition left in place; orphaned and unused, out of scope — see PHASE-1-IMPLEMENTATION-REPORT.md)
