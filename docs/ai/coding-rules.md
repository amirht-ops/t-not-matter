# Coding Rules

Version: 3.0

Status: Mandatory

Purpose:

This document defines implementation rules for all source code in the Enterprise Authorization Platform.

These rules apply to:

- Human Developers
    
- AI Agents
    
- Code Generators
    
- Automated Refactoring Tools
    

Violations require Architecture Review.

---

# Rule Hierarchy

When conflicts exist:

ADR

Specification

Project Context

Architecture Rules

Domain Glossary

Coding Rules

Coding Rules never override higher-level artifacts.

---

# Development Philosophy

Code must be:

- Secure
    
- Deterministic
    
- Observable
    
- Testable
    
- Maintainable
    
- Explicit
    

Code should optimize for correctness before convenience.

---

# Specification First Development

Implementation follows:

1. ADRs
    
2. Specifications
    
3. Contracts
    
4. Domain Model
    
5. Code
    

Never implement requirements that do not exist.

Never invent missing business rules.

Never invent permissions.

Never invent policies.

Never invent tenant rules.

When information is missing:

Stop and ask.

---

# Ubiquitous Language Rule

All code must use terminology defined in:

domain-glossary.md

Examples:

Use:

Permission

Not:

Capability

Use:

AuthorizationDecision

Not:

PermissionResult

Use:

Delegation

Not:

PermissionTransfer

Use domain language consistently.

---

# Bounded Context Rule

Code belongs to exactly one bounded context.

Examples:

Identity

Authorization

Policy

Tenant

Delegation

Audit

Business logic must never cross bounded context boundaries.

---

# Aggregate Rule

Aggregate Roots protect invariants.

All state changes must pass through aggregate behavior.

Allowed:

```csharp
role.AssignPermission(permission);
```

Forbidden:

```csharp
role.Permissions.Add(permission);
```

Aggregates own consistency.

External code may not bypass them.

---

# Entity Rule

Entities:

- Own behavior
    
- Protect state
    
- Preserve consistency
    

Entities are not DTOs.

Entities are not database records.

Entities must not expose mutable state.

---

# Value Object Rule

Use Value Objects whenever:

- Validation exists
    
- Equality by value exists
    
- Domain meaning exists
    

Good:

TenantId

RoleId

PermissionCode

Email

Bad:

string TenantId

string PermissionCode

Primitive obsession is prohibited.

---

# Domain Service Rule

Create Domain Services only when behavior:

- Belongs to the domain
    
- Does not belong to a single aggregate
    

Do not create services as utility classes.

---

# Domain Event Rule

Domain Events represent facts that already happened.

Naming:

Past Tense

Good:

RoleAssigned

PermissionGranted

DelegationCreated

Bad:

AssignRole

GrantPermission

CreateDelegation

Domain Events are immutable.

---

# Integration Event Rule

Integration Events are public contracts.

Requirements:

- Immutable
    
- Versioned
    
- Backward Compatible
    

Examples:

RoleAssignedV1

PermissionGrantedV1

Breaking changes require new versions.

---

# Authorization Rule

Authorization logic must never be implemented inside business services.

Forbidden:

```csharp
if(user.Role == "Admin")
```

Forbidden:

```csharp
if(user.HasPermission("invoice.create"))
```

Forbidden:

```csharp
if(usageCount > limit)
```

Authorization belongs to the authorization platform.

Business services request decisions.

---

# Authorization Request Rule

Authorization evaluation requires:

- Subject
    
- Action
    
- Resource
    
- Context
    

Never evaluate authorization using:

- Roles alone
    
- Claims alone
    
- Permissions alone
    

Authorization decisions require complete evaluation context.

---

# Resource Authorization Rule

Authorization must support:

- Resource Type Evaluation
    
- Resource Instance Evaluation
    

Examples:

invoice.read

invoice-123.read

Resource-scoped authorization is a first-class concept.

---

# Runtime Context Rule

Runtime context is provided by dedicated providers.

Runtime context may include:

- Tenant
    
- Environment
    
- Usage Counters
    
- Risk Signals
    
- Resource Attributes
    

Business services must not manually construct policy logic from context.

---

# Policy Rule

Policies are evaluated by OPA.

Application code must not duplicate policy behavior.

Forbidden:

```csharp
if(department == "Finance")
```

when the rule belongs to policy evaluation.

---

# Usage Constraint Rule

Usage limits are authorization concepts.

Examples:

Daily Limits

Monthly Limits

Per Resource Limits

Application services must never evaluate usage limits directly.

---

# CQRS Rule

Commands:

- Change State
    
- Produce Events
    

Queries:

- Read Data
    

Queries must not modify state.

Commands must not contain read-model concerns.

---

# Vertical Slice Rule

Every feature owns:

- Request
    
- Handler
    
- Validator
    
- Mapping
    
- Tests
    

Features must remain independent.

Do not create shared business managers.

---

# API Rule

API endpoints must be thin.

Allowed:

- Validation
    
- Mapping
    
- Dispatching
    

Forbidden:

- Business Logic
    
- Policy Logic
    
- Persistence Logic
    

---

# Repository Rule

Repositories abstract aggregates.

Repositories:

- Load Aggregates
    
- Save Aggregates
    

Repositories do not contain business rules.

Repositories do not implement workflows.

---

# Persistence Rule

PostgreSQL is source of truth.

Every aggregate root must contain:

- Id
    
- TenantId
    
- Version
    
- CreatedAt
    
- UpdatedAt
    

Optimistic concurrency is mandatory.

---

# Tenant Isolation Rule

Every operation must be tenant aware.

Required:

- TenantId in Aggregate Roots
    
- TenantId in Queries
    
- TenantId in Events
    
- TenantId in Cache Keys
    

Tenant isolation is not optional.

---

# Outbox Rule

Outbox Pattern is mandatory.

Required flow:

Persist State  
→ Persist Outbox Message  
→ Commit Transaction  
→ Publish Event

Direct event publishing is prohibited.

---

# Messaging Rule

RabbitMQ is used for integration events.

Consumers must:

- Be idempotent
    
- Tolerate duplicates
    
- Support retries
    

Never assume exactly-once delivery.

---

# Cache Rule

Redis is cache only.

Forbidden:

Business correctness depending on cache availability.

Cache misses must never break correctness.

---

# Error Handling Rule

Use explicit errors.

Avoid:

```csharp
throw new Exception();
```

Prefer:

DomainError

ValidationError

AuthorizationError

InfrastructureError

Errors must be meaningful.

---

# Logging Rule

Structured logging only.

Every log must include when available:

- CorrelationId
    
- TenantId
    
- UserId
    

Never log secrets.

Never log tokens.

Never log credentials.

---

# Security Rule

Default behavior:

Deny

If authorization cannot be evaluated:

Deny

If policy evaluation fails:

Deny

Fail Closed is mandatory.

---

# Testing Rule

Every feature requires:

Unit Tests

Integration Tests

Contract Tests

Critical authorization flows require:

End-to-End Tests

---

# Coverage Requirements

General Code:

> = 80%

Authorization Domain:

> = 95%

Security Critical Flows:

100%

---

# Naming Rules

Classes:

PascalCase

Methods:

PascalCase

Private Fields:

_camelCase

Async Methods:

Must end with Async

Examples:

GetRoleAsync

AssignPermissionAsync

EvaluateAuthorizationAsync

---

# AI Agent Restrictions

AI agents must never:

Invent Permissions

Invent Policies

Invent Tenant Rules

Invent Delegation Rules

Invent Event Contracts

Invent API Contracts

Invent Usage Constraints

Always consult:

- project-context.md
    
- architecture-rules.md
    
- domain-glossary.md
    
- specifications
    
- ADRs
    

before generating code.

When uncertain:

Ask.

Never hallucinate implementation details.