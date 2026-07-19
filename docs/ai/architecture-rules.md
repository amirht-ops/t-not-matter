# Architecture Rules

Version: 2.0

Status: Mandatory

Purpose:

This document defines non-negotiable architectural constraints for the Enterprise Authorization Platform.

Violations require Architecture Review and ADR approval.

---

# 1. Architecture Principles

The platform is built around the following priorities:

1. Security
    
2. Tenant Isolation
    
3. Authorization Correctness
    
4. Auditability
    
5. Reliability
    
6. Scalability
    
7. Performance
    

Performance improvements must never weaken security guarantees.

Availability must never override authorization correctness.

---

# 2. Architectural Style

The platform combines:

- Domain-Driven Design (DDD)
    
- Clean Architecture
    
- CQRS
    
- Vertical Slice Architecture
    
- Event-Driven Architecture
    

No alternative architectural style may be introduced without an ADR.

---

# 3. Bounded Context Rule

Each bounded context owns:

- Domain Model
    
- Database
    
- Events
    
- APIs
    

Examples:

- Identity
    
- Authorization
    
- Policy
    
- Delegation
    
- Tenant
    
- Audit
    

Bounded contexts are autonomous.

Direct database access between bounded contexts is prohibited.

Communication occurs through:

- APIs
    
- Integration Events
    

Only.

---

# 4. Authentication and Authorization Separation

Authentication and Authorization are independent concerns.

Identity Service is responsible for:

- Users
    
- Credentials
    
- Sessions
    
- MFA
    
- Authentication
    

Authorization Service is responsible for:

- Roles
    
- Permissions
    
- Decisions
    
- Effective Permissions
    

Authentication must never grant access.

Authorization must never authenticate users.

---

# 5. Authorization Architecture

Authorization is centralized.

Authorization logic must never exist inside business services.

Business services may:

- Request decisions
    
- Provide context
    

Business services may not:

- Evaluate permissions
    
- Evaluate roles
    
- Evaluate policies
    
- Evaluate quotas
    
- Evaluate usage limits
    

Authorization decisions are produced centrally.

---

# 6. Authorization Decision Model

Every authorization decision follows:

Decision =  
Subject

- Action
    
- Resource
    
- Context
    
- Policies
    

Authorization is not role based.

Authorization is permission based.

Roles only provide permissions.

Permissions participate in policy evaluation.

Policies participate in final decision generation.

---

# 7. Policy Architecture

Policies are externalized.

OPA is the policy engine.

Policy evaluation must occur outside business services.

OPA is responsible for:

- ABAC
    
- Dynamic Constraints
    
- Contextual Decisions
    
- Usage Rules
    

Services must never contain policy evaluation logic.

---

# 8. Usage-Constrained Authorization

Usage constraints are part of authorization.

Examples:

- Daily Limits
    
- Monthly Limits
    
- Resource Limits
    
- Action Limits
    

Authorization decisions may depend on:

- Current Usage
    
- Historical Usage
    
- Resource Access Counts
    

Usage evaluation belongs to Authorization + Policy layers.

Never inside business services.

---

# 9. Runtime Context Architecture

Authorization decisions may require runtime context.

Examples:

- Tenant
    
- User
    
- Resource
    
- Environment
    
- Usage Counters
    
- Time Windows
    

Runtime context is assembled by dedicated providers.

Business services must not construct authorization rules from context.

They only provide data.

---

# 10. Tenant Isolation Architecture

Tenant is a security boundary.

Every layer must enforce tenant isolation.

Required:

- TenantId in Aggregate Roots
    
- TenantId in Queries
    
- TenantId in Cache Keys
    
- TenantId in Events
    
- TenantId in Logs
    

Cross-tenant access is denied by default.

---

# 11. Domain Layer Rules

Domain is the center of the system.

Domain layer:

- Protects invariants
    
- Owns business rules
    
- Emits domain events
    

Domain must not depend on:

- EF Core
    
- Redis
    
- RabbitMQ
    
- OPA
    
- HTTP
    

Domain remains infrastructure independent.

---

# 12. Application Layer Rules

Application layer orchestrates use cases.

Responsibilities:

- Command Handling
    
- Query Handling
    
- Transaction Management
    
- Event Dispatch Preparation
    

Application layer does not contain:

- Persistence Logic
    
- Authorization Logic
    
- Policy Logic
    

---

# 13. Infrastructure Layer Rules

Infrastructure implements technical concerns.

Responsibilities:

- Persistence
    
- Messaging
    
- Caching
    
- External Integrations
    

Infrastructure never owns business rules.

---

# 14. API Layer Rules

API layer is an adapter.

Responsibilities:

- Request Validation
    
- Authentication Integration
    
- Contract Mapping
    

API layer never contains business logic.

---

# 15. Vertical Slice Architecture Rule

Every feature owns:

- Endpoint
    
- Request
    
- Validator
    
- Handler
    
- Tests
    

Features must remain isolated.

Feature-first organization is mandatory.

Technical-layer organization is prohibited.

---

# 16. CQRS Rule

Commands:

- Change State
    
- Enforce Invariants
    
- Produce Events
    

Queries:

- Read Data
    
- No Side Effects
    

Command and Query models must remain separated.

---

# 17. Event Architecture

Events are first-class architecture components.

Two event categories exist:

## Domain Events

Internal business facts.

## Integration Events

Cross-service contracts.

Integration Events are immutable.

Published contracts must never change.

Breaking changes require new versions.

---

# 18. Messaging Architecture

RabbitMQ is the event transport.

Required:

- Durable Queues
    
- Retry Policies
    
- Dead Letter Queues
    
- Publisher Confirms
    

Consumers must be idempotent.

Duplicate delivery must be tolerated.

---

# 19. Outbox Architecture

Outbox Pattern is mandatory.

State changes and event publication must be coordinated.

Flow:

Transaction  
→ Outbox Record  
→ Commit  
→ Publish

Direct event publishing is prohibited.

---

# 20. Persistence Architecture

Database per bounded context.

Shared databases are prohibited.

PostgreSQL is source of truth.

Redis is cache only.

Caches must be disposable.

System correctness must never depend on cache availability.

---

# 21. Cache Architecture

Redis is used for:

- Effective Permissions
    
- Authorization Decisions
    
- Runtime Context
    
- Read Models
    

Cache invalidation must be event-driven.

Redis must never become the system of record.

---

# 22. Audit Architecture

Every security-sensitive operation must be auditable.

Examples:

- Permission Assignment
    
- Role Assignment
    
- Delegation Creation
    
- Policy Changes
    
- Authorization Decisions
    

Audit records are append-only.

Audit history must never be modified.

---

# 23. Fail Closed Rule

When authorization cannot be evaluated:

Decision = Deny

Examples:

- OPA unavailable
    
- Runtime Context unavailable
    
- Permission Resolution failure
    
- Policy Evaluation timeout
    

System must fail closed.

Never fail open.

---

# 24. Observability Architecture

Every operation must be observable.

Required:

- Structured Logging
    
- Distributed Tracing
    
- Metrics
    

Required correlation fields:

- CorrelationId
    
- TenantId
    
- UserId
    

Authorization decisions must be traceable end-to-end.

---

# 25. Contract First Architecture

Contracts are authoritative.

Includes:

- API Contracts
    
- Event Contracts
    
- Domain Contracts
    

Implementation follows contracts.

Contracts never follow implementation.

---

# 26. Technology Responsibilities

OPA:

- Policy Evaluation
    

Kong:

- API Gateway
    
- Routing
    
- TLS
    
- Rate Limiting
    

RabbitMQ:

- Event Transport
    

Redis:

- Cache
    

PostgreSQL:

- Persistence
    

OpenTelemetry:

- Observability
    

Technology responsibilities must not overlap.

---

# 27. AI Agent Constraints

AI Agents must never:

- Invent permissions
    
- Invent policies
    
- Invent quotas
    
- Invent tenant rules
    
- Invent event contracts
    

Always read:

1. project-context.md
    
2. architecture-rules.md
    
3. coding-rules.md
    
4. domain-glossary.md
    
5. ADRs
    
6. Specifications
    

Architecture Rules override Coding Rules when conflicts exist.