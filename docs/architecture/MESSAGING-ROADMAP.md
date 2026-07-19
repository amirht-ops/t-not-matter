# Messaging Roadmap

> This file is **NOT a TODO**. It holds future/architecture work that requires a new ADR or architectural decision before implementation. Anything depending on an unresolved decision is marked **Blocked by Architecture Decision**.

## 1. Shared Event Contracts + EventCatalog
- **Why valuable:** Eliminates magic-string `EventType` duplication between producers and consumers; gives compile-time safety so a rename cannot silently break a consumer.
- **Why NOT now:** Requires choosing the contract surface (typed records vs constants-only) and a shared assembly boundary — an ADR.
- **Prerequisite ADR:** "Integration Event Contract Ownership & Packaging".

## 2. Platform MassTransit Bootstrap (`AddPlatformMassTransit`)
- **Why valuable:** Single place for host, exchange, retry, quorum, DLQ config → guarantees parity across all three services and prevents the IdentityService gap from recurring.
- **Why NOT now:** Depends on the contract/assembly decision and a consistency standard.
- **Prerequisite ADR:** Same as #1; plus "Standard MassTransit Configuration Contract".

## 3. Topic Routing Keys / Routing Partition
- **Why valuable:** Stops the current full fan-out (every queue receives every event); reduces I/O/CPU and hides unrelated events from consumers.
- **Why NOT now:** Requires agreeing a routing-key taxonomy (`identity.user`, `tenant.department`, …) and rebinding all queues — a topology change.
- **Prerequisite ADR:** "RabbitMQ Routing Key Taxonomy & Queue Binding Strategy".

## 4. Event Versioning + Schema Registry / Upcasters
- **Why valuable:** `Version` is hardcoded to 1 today; real versioning/upcasting enables safe schema evolution.
- **Why NOT now:** Needs a versioning policy and (optionally) a schema registry decision.
- **Prerequisite ADR:** "Event Versioning & Schema Evolution Policy".

## 5. Authorization Local Decision Cache (decouple sync)
- **Why valuable:** Login and tenant mutations currently block on synchronous HTTP to AuthorizationService; a local cache makes services independently available and removes cascading outages.
- **Why NOT now:** Requires a caching/invalidation strategy and a fail-open vs fail-closed policy decision.
- **Prerequisite ADR:** "Authorization Decision Caching & Availability Policy". **Blocked by Architecture Decision.**

## 6. Durable Audit & Analytics Sinks
- **Why valuable:** Current `LoggingAuthorizationAuditSink` / `AnalyticsEventSink` only log; dedicated pipelines persist nothing — no durable audit trail.
- **Why NOT now:** Requires choosing a store/stream and retention policy.
- **Prerequisite ADR:** "Audit & Analytics Persistence Target".

## 7. Orphaned / Ignored Event Resolution
- **Why valuable:** `TenantNameUpdatedV1`, `DepartmentNameUpdatedV1`, `DepartmentDescriptionUpdatedV1`, `SessionCreatedV1`, `SessionRefreshTokenRotatedV1`, `UserSynchronizedV1` are produced but unhandled → stale caches / missing projections.
- **Why NOT now:** Each needs a per-event decision: add a consumer vs stop publishing.
- **Prerequisite ADR:** Per-event "Produce-or-Drop" decision. **Blocked by Architecture Decision** (per event).

## 8. Redundant Cache Invalidation Reconciliation
- **Why valuable:** Handlers invalidate cache synchronously *and* via events (double invalidation); picking one path simplifies and clarifies ownership.
- **Why NOT now:** Coupled to #5 (local cache) and #7 decisions.
- **Prerequisite ADR:** Depends on #5/#7.

## 9. Naming Standardization (`kebab-case.vN`)
- **Why valuable:** Today mixed `PascalCaseV1` vs `kebab-case.v1`; a single convention aids tooling and the EventCatalog.
- **Why NOT now:** Should land together with #1/#4 to avoid double migration.
- **Prerequisite ADR:** Tied to #1/#4.

## 10. Dead Domain-Event Cleanup (~12 events) + `UsageAccountingService`
- **Why valuable:** Declared-but-unproduced events (`role-activated/deactivated/parent-changed`, `permission-deprecated/archived/version-created`, `evaluated`, `usage-incremented`, `MfaDisabled`, `PasswordChanged`, `PasswordReset`, `UserLoggedIn`) and an unused `UsageAccountingService` are code rot.
- **Why NOT now:** Removing them is a product decision (are they planned features?); renaming/keeping needs #1.
- **Prerequisite ADR:** "Lifecycle Policy for Unimplemented Domain Events". **Blocked by Architecture Decision.**

## 11. OPA Divergence Monitoring / DLQ Replay Tooling
- **Why valuable:** If `OpaSyncConsumer` exhausts retries, OPA policy data drifts; needs alerting + replay.
- **Why NOT now:** Requires observability/tooling decisions; pairs with #3.
- **Prerequisite ADR:** "Messaging Observability & DLQ Replay Strategy".

## 12. Metrics / Observability for Messaging
- **Why valuable:** No current metrics on publish/consume latency, DLQ depth, or fan-out volume.
- **Why NOT now:** Needs a metrics platform decision.
- **Prerequisite ADR:** "Messaging Metrics & SLO Definition".
