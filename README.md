# Enterprise Authorization Platform

**Version:** 1.0 | **Stack:** ASP.NET Core 10 · C# 13 · PostgreSQL · Redis · RabbitMQ · OPA · Kong

---

## Architecture Overview

The platform uses a **tenant-aware, policy-driven authorization architecture** where authorization decisions are externalized and evaluated outside business services.

Authorization decisions follow the model:

Can Subject perform Action on Resource under Context?
Decision = Subject + Action + Resource + Context + Policies

This means authorization is not limited to static role-permission checks. The platform supports:

- **Tenant-scoped RBAC** for base role and permission assignment
- **ABAC / context-aware authorization** for evaluating runtime attributes
- **Usage-based authorization** for quota and rate-constrained access
- **Resource-scoped limits** for restrictions tied to a specific resource instance

Examples of supported policies include:

- A supervisor may create invoices only **100 times per month**
- A supervisor may edit invoices only **1 time per day**
- A supervisor may view the same invoice only **3 times per day**

Business services must never embed authorization logic. They only provide the required authorization input and invoke the centralized authorization decision engine.

---

## Policy Evaluation and Runtime Context

Authorization policies are defined externally in OPA and evaluated using runtime context supplied by the platform.

The runtime context may include:

- tenant identity
- subject identity
- roles and effective permissions
- service name
- action name
- resource type and resource id
- environment attributes
- usage counters
- daily/monthly quota values
- resource-specific access counts
- request timestamp and evaluation window

This enables the platform to evaluate decisions such as:

- whether a subject has the base permission
- whether the request belongs to the correct tenant
- whether the operation is still within allowed usage limits
- whether the request satisfies contextual policy constraints

---
## Usage-Constrained Authorization

Some access decisions require **stateful contextual evaluation**.

In such cases, the authorization engine evaluates not only roles and policies, but also **usage-related context**, such as:

- how many times a user already performed an action today
- how many times a user performed an action in the current month
- how many times a user accessed a specific resource during a time window

The platform handles this by separating the flow into two stages:

### 1. Pre-Authorization Check
Before the business operation executes:

- the caller builds the authorization request
- usage context is retrieved from the runtime context provider / counter store
- OPA evaluates the complete input
- the result is `Allow` or `Deny`

### 2. Post-Success Accounting
Only after the business operation succeeds:

- a domain/integration event is emitted through the Outbox Pattern
- usage counters are updated
- audit evidence is recorded

This ensures quota and access counters are only increased for successful operations.

---
## Responsibilities

### Business Services
Business services:

- must not contain authorization policy logic
- must not evaluate quotas locally
- must not encode tenant-specific access rules internally
- must call the authorization decision engine
- may provide business context required for evaluation

### Authorization / Policy Layer
The authorization and policy layer is responsible for:

- role and permission evaluation
- tenant-aware assignment resolution
- contextual policy evaluation
- usage/quota decisioning
- centralized allow/deny outcomes

### Runtime Usage Context Provider
A runtime usage context provider may supply stateful context such as:

- daily action counts
- monthly action counts
- per-resource daily access counts
- quota windows
- other evaluation-time environmental attributes

This context is provided to the policy engine as part of the authorization input and is not implemented as business logic inside application services.

---

## Example: Warehouse Service

A Warehouse Service may request authorization for:

- `invoice.create`
- `invoice.edit`
- `invoice.read`

Example constraints for tenant `A`, role `Supervisor`:

- `invoice.create` allowed up to **100 times per month**
- `invoice.edit` allowed up to **1 time per day**
- `invoice.read` allowed up to **3 times per day per invoice**

The Warehouse Service does not implement these rules itself. It sends the relevant subject, tenant, action, resource, and runtime context to the authorization engine, and the final decision is evaluated by OPA.

---

## Notes on Consistency and Reliability

Usage-constrained authorization must preserve correctness under retries, failures, and concurrency. Therefore:

- authorization check happens before execution
- counter update happens only after successful completion
- post-success updates must flow through the Outbox Pattern
- audit evidence must remain append-only
- implementation details for atomicity, concurrency, and time-window boundaries must be defined in technical specifications


---

## Solution Structure

```
EnterpriseAuthPlatform/
├── docs/                          # ADRs, API contracts, domain specs, diagrams
│   ├── adr/                       # Architecture Decision Records (read before coding)
│   ├── specs/
│   │   ├── api/                   # API contracts — versioned, immutable (Rule 10)
│   │   ├── events/                # Integration event contracts
│   │   └── domain/                # Domain contracts
│   └── diagrams/
│
├── deploy/                        # GitOps / Kubernetes / Helm
│   ├── helm/                      # Helm chart — one chart, per-env values
│   │   └── values/
│   │       ├── values.yaml        # Shared defaults
│   │       ├── values.dev.yaml
│   │       └── values.prod.yaml
│   ├── k8s/
│   │   ├── base/                  # Kustomize base manifests
│   │   └── overlays/{dev,staging,prod}/
│   └── gitops/                    # Flux/ArgoCD application definitions
│
├── observability/                 # OpenTelemetry + Prometheus + Grafana + Loki + Tempo
│   ├── otel-collector/config.yaml
│   ├── prometheus/
│   ├── grafana/dashboards/
│   ├── loki/
│   └── tempo/
│
├── opa/                           # Externalized policies (Architecture Rule 4 — MANDATORY)
│   ├── policies/
│   │   ├── authorization/authz.rego   # ABAC policies — OPA evaluates, services NEVER embed
│   │   ├── delegation/
│   │   └── tenant/
│   ├── data/                      # Policy data bundles
│   └── tests/                     # OPA unit tests
│
├── gateway/
│   └── kong/                      # Kong config — routing only (Architecture Rule 3: Kong is NOT an auth engine)
│       ├── declarative/kong.yml
│       └── plugins/
│
├── tools/
│   └── scripts/                   # Migration generators, test runners, linters
│
└── src/
    ├── SharedKernel/              # Cross-cutting primitives — no business logic
    │   ├── Domain/
    │   │   ├── Primitives/        # AggregateRoot, Entity, ValueObject
    │   │   ├── Events/            # IDomainEvent, IIntegrationEvent
    │   │   ├── Errors/            # Result<T>, Error
    │   │   └── Guards/
    │   ├── Contracts/
    │   │   ├── Events/            # EventEnvelope
    │   │   └── Api/               # ApiResponse<T>, PaginatedResponse<T>
    │   └── Infrastructure/
    │       ├── Outbox/            # OutboxMessage — Outbox Pattern (mandatory)
    │       └── Messaging/
    │
    └── Services/                  # 6 bounded contexts — each fully independent
        ├── Identity/              # Authentication: Login, MFA, Sessions, Tokens
        ├── Authorization/         # RBAC: Roles, Permissions, Groups, Effective Permissions
        ├── Policy/                # ABAC: Dynamic rules, Risk evaluation → syncs to OPA
        ├── Tenant/                # Isolation, Membership
        ├── Audit/                 # Compliance, immutable security logging
        └── Delegation/            # Temporary permission transfer (always with expiry)
```

---

## Per-Service Structure (Clean Architecture + Vertical Slices)

Every service follows this identical structure:

```
Services/{ServiceName}/
│
├── {Service}.Domain.csproj           # Zero dependencies
│   └── Domain/
│       ├── Aggregates/               # Aggregate roots — protect invariants, emit events
│       │   └── {Aggregate}/
│       │       ├── {Aggregate}.cs    # AggregateRoot (extends SharedKernel)
│       │       └── {ChildEntity}.cs
│       ├── Events/                   # Domain events (Rule 7: every state change)
│       ├── ValueObjects/             # Immutable, equality-by-value
│       ├── Repositories/             # Interfaces only — no implementations
│       └── Services/                 # Domain service interfaces
│
├── {Service}.Application.csproj      # Depends on Domain only
│   └── Application/
│       ├── Features/                 # Vertical slices — one folder per use case
│       │   └── {FeatureName}/
│       │       ├── {Feature}Command.cs       # or Query.cs
│       │       ├── {Feature}CommandHandler.cs
│       │       ├── {Feature}CommandValidator.cs
│       │       └── {Feature}Response.cs
│       └── Common/
│           ├── Abstractions/         # IUnitOfWork, etc.
│           └── Behaviors/            # MediatR pipeline behaviors
│               ├── ValidationBehavior.cs
│               ├── AuditBehavior.cs       # Rule 8: every decision auditable
│               ├── TenantBehavior.cs      # Rule 5: tenant context mandatory
│               └── FailClosedBehavior.cs  # Rule 9: fail closed (Authorization only)
│
├── {Service}.Infrastructure.csproj   # Depends on Application
│   └── Infrastructure/
│       ├── Persistence/
│       │   ├── {Service}DbContext.cs
│       │   ├── Configurations/       # EF Core Fluent API — TenantId filter mandatory
│       │   ├── Repositories/         # Interface implementations
│       │   └── Migrations/
│       ├── Messaging/
│       │   ├── Publishers/           # RabbitMQ via MassTransit + Publisher Confirm
│       │   └── Consumers/            # Inbound integration events
│       ├── Outbox/
│       │   └── OutboxProcessor.cs    # Outbox Pattern — prevents lost messages
│       └── Caching/                  # Redis — never source of truth (Coding Rules)
│
├── {Service}.Api.csproj              # Depends on Application (never Infrastructure directly)
│   └── Api/
│       ├── Program.cs
│       ├── Endpoints/                # Minimal API endpoint groups
│       │   └── {Feature}Endpoints.cs  # Versioned: /api/v1/{resource}
│       ├── Middleware/
│       │   ├── CorrelationIdMiddleware.cs   # X-Correlation-Id mandatory
│       │   ├── TenantMiddleware.cs          # X-Tenant-Id mandatory (Rule 5)
│       │   └── ExceptionHandlingMiddleware.cs
│       ├── Contracts/
│       │   ├── Requests/
│       │   └── Responses/
│       └── Extensions/
│           └── ServiceCollectionExtensions.cs
│
└── {Service}.Tests.csproj
    └── Tests/
        ├── Unit/
        │   ├── Domain/               # Aggregate invariant tests (no infra)
        │   └── Application/          # Handler tests with mocked infra
        ├── Integration/              # API endpoint tests with real DB
        ├── Contract/                 # Pact consumer/provider contract tests
        └── E2E/                      # Critical authorization flows (Authorization + Delegation only)
```

---

## Mandatory Architecture Rules (Quick Reference)

| Rule | Enforcement Point |
|------|-------------------|
| Authentication ≠ Authorization | Separate services, separate DBs, separate bounded contexts |
| Business Services never authorize | `IAuthorizationDecisionEngine` interface — call don't embed |
| Kong is not an auth engine | kong.yml contains routes only |
| Policies externalized to OPA | `opa/policies/` — services call OPA via `OpaHttpClient` |
| Every request needs TenantId | `TenantMiddleware` → `TenantBehavior` pipeline |
| No cross-service DB access | APIs + RabbitMQ integration events only |
| Every state change emits event | `AggregateRoot.RaiseDomainEvent()` |
| Every security decision auditable | `AuditBehavior` + Audit service consumers |
| Fail Closed | `FailClosedBehavior` + `OpaHttpClient` default Deny |
| Immutable contracts | New version = new route `/api/v2/...` |
| Outbox Pattern | `OutboxProcessor` in every service |
| Structured logging only | JSON, with CorrelationId + TenantId + UserId |

---

## Technology Responsibilities

| Technology | Role | NOT responsible for |
|------------|------|---------------------|
| Kong | API routing, rate limiting, TLS termination | Authorization logic |
| OPA | ABAC policy evaluation | Routing, business logic |
| Redis | Caching effective permissions | Source of truth |
| RabbitMQ | Integration event bus | Request/response patterns |
| PostgreSQL | Persistent state per service | Shared cross-service access |
| OpenTelemetry | Traces + metrics + logs | Business logic |

---

## Coverage Requirements

| Scope | Minimum |
|-------|---------|
| General | 80% |
| Authorization Domain | 95% |
| Critical auth flows | E2E required |
