# Folder Structure

Version: 2.0

Status: Mandatory

Purpose:

This document defines the official solution structure of the Enterprise Authorization Platform.

All source code, tests, contracts, specifications, infrastructure assets, and generated code must follow this structure.

Deviation requires Architecture Review.

---

# Design Principles

The folder structure is designed around:

- Bounded Contexts
    
- Vertical Slices
    
- Clean Architecture
    
- CQRS
    
- Event-Driven Architecture
    

Folders represent ownership boundaries.

Folders are not organizational preferences.

They are architecture constraints.

---

# Repository Root

```text
EnterpriseAuthPlatform/
```

Root contains:

- Documentation
    
- Infrastructure
    
- Platform Assets
    
- Source Code
    

Business code must exist only under:

```text
src/
```

---

# Top Level Structure

```text
EnterpriseAuthPlatform/

├── docs/
├── deploy/
├── observability/
├── opa/
├── gateway/
├── tools/
└── src/
```

---

# docs/

Purpose:

Project knowledge repository.

Contains:

```text
docs/

├── adr/
├── specs/
├── diagrams/
└── ai-context/
```

---

# docs/adr

Architecture Decision Records.

Purpose:

Record architectural decisions.

Examples:

```text
ADR-001-DDD.md
ADR-002-Outbox.md
ADR-003-MultiTenant.md
```

Read before implementation.

ADRs override implementation assumptions.

---

# docs/specs

System specifications.

Contains:

```text
specs/

├── api/
├── domain/
└── events/
```

Specifications are authoritative.

Implementation follows specifications.

Never the reverse.

---

# docs/specs/api

API contracts.

Contains:

```text
CreateRole.md
AssignPermission.md
EvaluateAuthorization.md
```

API implementation must match contracts.

---

# docs/specs/domain

Domain contracts.

Contains:

```text
RoleContract.md
PermissionContract.md
DelegationContract.md
```

Domain model follows these definitions.

---

# docs/specs/events

Integration event contracts.

Examples:

```text
RoleAssignedV1.md
PermissionGrantedV1.md
DelegationCreatedV1.md
```

Published contracts are immutable.

---

# docs/diagrams

Architecture diagrams.

Examples:

```text
ContextMap.drawio
AuthorizationFlow.drawio
TenantFlow.drawio
```

---

# docs/ai-context

AI guidance documents.

Contains:

```text
project-context.md
architecture-rules.md
coding-rules.md
domain-glossary.md
folder-structure.md
```

Must be loaded before AI code generation.

---

# deploy/

Deployment assets.

Contains:

```text
deploy/

├── helm/
├── k8s/
└── gitops/
```

No application code allowed.

---

# observability/

Observability platform configuration.

Contains:

```text
observability/

├── otel-collector/
├── prometheus/
├── grafana/
├── loki/
└── tempo/
```

No business logic allowed.

---

# opa/

Policy system.

Contains:

```text
opa/

├── policies/
├── data/
└── tests/
```

---

# opa/policies

OPA policy definitions.

Examples:

```text
authorization/
delegation/
tenant/
usage/
```

OPA owns policy logic.

Services must never duplicate policy logic.

---

# gateway/

API gateway configuration.

Contains:

```text
gateway/kong/
```

Kong performs:

- Routing
    
- TLS
    
- Rate Limiting
    

Kong does not perform authorization.

---

# tools/

Engineering tooling.

Examples:

```text
scripts/
generators/
linters/
```

No runtime business code.

---

# src/

Contains all production source code.

```text
src/

├── SharedKernel/
└── Services/
```

---

# SharedKernel

Purpose:

Cross-cutting primitives.

Contains:

```text
SharedKernel/

├── Domain/
├── Contracts/
└── Infrastructure/
```

SharedKernel is intentionally small.

Business logic is prohibited.

---

# SharedKernel/Domain

Contains reusable primitives.

Examples:

```text
AggregateRoot
Entity
ValueObject
Result
Error
DomainEvent
```

Only abstractions allowed.

---

# SharedKernel/Contracts

Contains shared contracts.

Examples:

```text
EventEnvelope
ApiResponse
PagedResponse
```

No business-specific contracts.

---

# SharedKernel/Infrastructure

Contains infrastructure abstractions.

Examples:

```text
Outbox
Messaging
Clock
Correlation
```

No domain rules.

---

# Services

Contains bounded contexts.

```text
Services/

├── Identity/
├── Authorization/
├── Policy/
├── Tenant/
├── Audit/
└── Delegation/
```

Each bounded context is independent.

---

# Service Ownership Rule

Each service owns:

- Domain
    
- Database
    
- Events
    
- APIs
    
- Read Models
    

No shared databases.

No shared domain models.

---

# Service Internal Structure

Every service follows:

```text
Service/

├── Domain/
├── Application/
├── Infrastructure/
├── Api/
└── Tests/
```

No exceptions.

---

# Domain Layer

Purpose:

Business model.

Contains:

```text
Domain/

├── Aggregates/
├── Entities/
├── ValueObjects/
├── Events/
├── Repositories/
└── Services/
```

---

# Domain/Aggregates

Aggregate roots only.

Examples:

```text
Role/
Permission/
Delegation/
Tenant/
```

Aggregate roots enforce invariants.

---

# Domain/Events

Domain Events only.

Examples:

```text
RoleAssigned
PermissionGranted
DelegationCreated
```

Integration Events do not belong here.

---

# Domain/Repositories

Repository interfaces only.

No implementations.

---

# Application Layer

Purpose:

Use Case orchestration.

Contains:

```text
Application/

├── Features/
└── Common/
```

---

# Application/Features

Vertical Slice Architecture.

Each feature owns:

```text
Feature/

├── Command.cs
├── Handler.cs
├── Validator.cs
├── Response.cs
└── Mapping.cs
```

or

```text
Feature/

├── Query.cs
├── Handler.cs
├── Response.cs
└── Mapping.cs
```

Feature folders are mandatory.

---

# Feature Naming Rule

Use business language.

Good:

```text
AssignRole
GrantPermission
CreateDelegation
EvaluateAuthorization
```

Bad:

```text
RoleManager
PermissionUtils
CommonHandlers
```

---

# Application/Common

Contains:

```text
Abstractions/
Behaviors/
Exceptions/
```

Only reusable application concerns.

---

# Infrastructure Layer

Purpose:

Technical implementations.

Contains:

```text
Infrastructure/

├── Persistence/
├── Messaging/
├── Caching/
├── Outbox/
└── ExternalServices/
```

---

# Infrastructure/Persistence

Contains:

```text
DbContext
Configurations
Repositories
Migrations
```

EF Core configuration belongs here.

---

# Infrastructure/Messaging

Contains:

```text
Consumers
Publishers
EventMappings
```

Integration Events belong here.

---

# Infrastructure/Caching

Redis implementation only.

No business logic.

---

# Infrastructure/Outbox

Contains:

```text
OutboxProcessor
OutboxMessage
```

Required in every service.

---

# Api Layer

Purpose:

External entry point.

Contains:

```text
Api/

├── Endpoints/
├── Contracts/
├── Middleware/
└── Extensions/
```

---

# Api/Endpoints

Feature endpoints.

Examples:

```text
AssignRoleEndpoints
CreateDelegationEndpoints
EvaluateAuthorizationEndpoints
```

Minimal APIs preferred.

---

# Api/Contracts

Contains:

```text
Requests/
Responses/
```

External API models only.

Never expose domain entities.

---

# Api/Middleware

Examples:

```text
CorrelationIdMiddleware
TenantMiddleware
ExceptionMiddleware
```

Cross-cutting concerns only.

---

# Tests

Purpose:

Verification.

Contains:

```text
Tests/

├── Unit/
├── Integration/
├── Contract/
└── E2E/
```

---

# Unit Tests

Validate:

- Aggregates
    
- Value Objects
    
- Domain Logic
    

No infrastructure dependencies.

---

# Integration Tests

Validate:

- Database
    
- Messaging
    
- API Integration
    

Real infrastructure.

---

# Contract Tests

Validate:

- API Contracts
    
- Event Contracts
    

Contracts must not drift.

---

# E2E Tests

Validate:

- Authorization Flows
    
- Delegation Flows
    
- Tenant Isolation
    
- Usage Constraints
    

Critical paths only.

---

# Dependency Rules

Allowed:

```text
Api
 ↓
Application
 ↓
Domain

Infrastructure
 ↓
Application
 ↓
Domain
```

---

# Forbidden Dependencies

Domain → Application

Domain → Infrastructure

Domain → Api

Application → Api

Application → Infrastructure

Api → Infrastructure

Cross-Service Domain References

Shared Database Access

---

# Naming Rules

Folders:

PascalCase

Examples:

```text
Authorization
RoleManagement
GrantPermission
```

Files:

PascalCase

Examples:

```text
CreateRoleCommand.cs
CreateRoleHandler.cs
RoleAssigned.cs
```

---

# Generated Code Rule

AI-generated code must be placed only inside the correct bounded context and feature folder.

AI agents must never:

- Create Common folders for business logic
    
- Create Shared Services for domain behavior
    
- Create Generic Managers
    
- Create Utility-based domain abstractions
    

Business logic always belongs to the owning feature and bounded context.