# Domain Glossary

Version: 2.0

Status: Canonical Ubiquitous Language

Purpose:

This document defines the official language of the Enterprise Authorization Platform.

All specifications, ADRs, APIs, events, code, tests, documentation, and AI-generated artifacts must use these terms consistently.

If a term is not defined here, it is not considered part of the domain language.

---

# Core Domain

The platform exists to answer one question:

Can a Subject perform an Action on a Resource under a given Context?

The answer is an Authorization Decision.

Everything in the system exists to support this decision.

---

# Authorization Decision

Definition:

The final allow or deny outcome produced by the authorization engine.

Output:

- Allow
    
- Deny
    

An authorization decision is the final authority.

Business services must never override it.

---

# Subject

Definition:

The actor requesting access.

Examples:

- User
    
- Service Account
    
- External System
    

A Subject initiates an authorization request.

Subjects do not receive permissions directly.

Permissions are acquired through assignments.

---

# Principal

Definition:

The authenticated representation of a subject.

A Principal contains identity information required for authorization.

Examples:

- SubjectId
    
- TenantId
    
- Claims
    
- Session Information
    

Principal is an execution identity.

Subject is a domain identity.

---

# Tenant

Definition:

A logical security boundary.

A tenant owns:

- Users
    
- Roles
    
- Permissions
    
- Policies
    
- Resources
    

Tenant isolation is mandatory.

Cross-tenant access is denied by default.

---

# Tenant Boundary

Definition:

The security perimeter that prevents data and authorization leakage between tenants.

Violating tenant boundaries is considered a critical security failure.

---

# Resource

Definition:

An object being accessed.

Examples:

- Invoice
    
- Product
    
- Warehouse
    
- Report
    
- Customer
    

Authorization is evaluated against a resource.

---

# Resource Type

Definition:

A classification of resources.

Examples:

invoice  
customer  
warehouse  
report

Used during policy evaluation.

---

# Resource Identifier

Definition:

The unique identifier of a specific resource instance.

Examples:

Invoice-123  
Customer-456

Used for resource-scoped authorization.

---

# Action

Definition:

The operation requested against a resource.

Examples:

create  
read  
update  
delete  
approve  
publish

Actions are evaluated during authorization.

---

# Permission

Definition:

The smallest authorization unit.

Permissions are atomic.

Examples:

invoice.create  
invoice.read  
invoice.update  
invoice.delete

Permissions are not decomposed.

Authorization decisions ultimately depend on permissions.

---

# Permission Assignment

Definition:

A relationship that grants permissions to a subject indirectly.

Permissions may be acquired through:

- Roles
    
- Delegations
    
- Policies
    

Permissions define capability.

Assignments define ownership.

---

# Effective Permission

Definition:

The final permission set available to a subject after evaluation.

Includes:

- Direct grants
    
- Role grants
    
- Delegated grants
    
- Policy modifications
    

Effective permissions are calculated values.

They are not persisted as source-of-truth entities.

---

# Role

Definition:

A collection of permissions.

Roles simplify permission management.

Roles never authorize access directly.

Permissions authorize access.

---

# Role Assignment

Definition:

The act of attaching a role to a subject.

Role assignments influence effective permissions.

---

# Role Hierarchy

Definition:

A structure where one role inherits permissions from another role.

Example:

Administrator  
→ Manager  
→ Employee

Permission inheritance must remain explicit and traceable.

---

# Delegation

Definition:

Temporary transfer of permissions from one subject to another.

Delegation is time-bound.

Delegation is revocable.

Delegation does not create ownership.

Delegation creates temporary authority.

---

# Delegator

Definition:

The subject granting authority.

---

# Delegatee

Definition:

The subject receiving authority.

---

# Delegated Permission

Definition:

A permission obtained through delegation.

Delegated permissions may expire automatically.

---

# Policy

Definition:

A rule that modifies authorization behavior.

Policies evaluate runtime context.

Policies are externalized.

Policies are evaluated by OPA.

---

# Policy Evaluation

Definition:

The process of evaluating policies against authorization input.

Inputs:

- Subject
    
- Resource
    
- Action
    
- Context
    

Outputs:

- Allow
    
- Deny
    

---

# Policy Engine

Definition:

The component responsible for policy evaluation.

Current implementation:

OPA

Policy evaluation must never occur inside business services.

---

# Context

Definition:

Runtime information used during authorization.

Examples:

- Tenant
    
- Environment
    
- Usage Counters
    
- Current Time
    
- Resource Attributes
    

Context influences authorization outcomes.

---

# Attribute

Definition:

A contextual property used during policy evaluation.

Examples:

Department  
RiskLevel  
Region  
Environment

Attributes are evaluated dynamically.

---

# ABAC

Attribute-Based Access Control.

Authorization based on:

- Subject Attributes
    
- Resource Attributes
    
- Environment Attributes
    

ABAC extends permission-based authorization.

ABAC does not replace permissions.

---

# RBAC

Role-Based Access Control.

Authorization model based on:

Roles  
→ Permissions

RBAC provides baseline permissions.

RBAC alone is insufficient for this platform.

---

# Usage Constraint

Definition:

A limitation based on historical usage.

Examples:

100 creates per month

3 reads per day

1 edit per day

Usage constraints participate in authorization decisions.

---

# Quota

Definition:

A measurable usage limit.

Examples:

Monthly Create Limit

Daily Read Limit

Per Resource Access Limit

Quotas are evaluated before operation execution.

---

# Usage Counter

Definition:

The recorded usage associated with a constraint.

Examples:

invoice.create = 42

invoice.read = 3

Counters are runtime state.

Counters are not business permissions.

---

# Resource Scoped Limit

Definition:

A limit applied to a specific resource instance.

Example:

A subject may read invoice 123 only three times per day.

---

# Authorization Request

Definition:

The complete input submitted for evaluation.

Contains:

- Subject
    
- Action
    
- Resource
    
- Context
    

Authorization requests are immutable.

---

# Authorization Result

Definition:

The output of policy evaluation.

Contains:

- Decision
    
- Evaluation Evidence
    
- Evaluation Metadata
    

---

# Authorization Evidence

Definition:

Supporting information explaining why a decision was produced.

Used for:

- Auditing
    
- Compliance
    
- Investigation
    

---

# Audit Record

Definition:

An immutable historical record.

Audit records are append-only.

Audit records must never be modified.

---

# Domain Event

Definition:

An immutable record of a business fact.

Examples:

RoleAssigned  
PermissionGranted  
DelegationCreated

Domain events occur inside bounded contexts.

---

# Integration Event

Definition:

An immutable cross-service contract.

Examples:

RoleAssignedV1  
PermissionGrantedV1  
DelegationCreatedV1

Integration events are externally visible.

Breaking changes require versioning.

---

# Aggregate

Definition:

The consistency boundary of the domain.

Aggregates protect invariants.

Aggregates emit domain events.

Aggregates are modified through behavior.

Not data structures.

---

# Aggregate Root

Definition:

The entry point to an aggregate.

All modifications must pass through the aggregate root.

---

# Entity

Definition:

An object identified by identity.

Identity remains stable over time.

Entities own behavior.

---

# Value Object

Definition:

An immutable object defined by value.

Examples:

TenantId  
PermissionCode  
Email  
RoleName

Value objects have no identity.

---

# Invariant

Definition:

A business rule that must always remain true.

Aggregates exist primarily to protect invariants.

Invariant violations are domain failures.

---

# Outbox Pattern

Definition:

A reliability mechanism ensuring state changes and event publication remain consistent.

Flow:

Persist State  
→ Persist Outbox Record  
→ Commit  
→ Publish

Mandatory for all integration events.

---

# Fail Closed

Definition:

Security-first behavior.

If authorization cannot be evaluated:

Decision = Deny

Never allow access because of uncertainty.

---

# Source Of Truth

Definition:

The authoritative owner of a piece of information.

Examples:

PostgreSQL = Persistent State

OPA = Policy Evaluation

Authorization Service = Authorization Decisions

Redis is never a source of truth.

---

# Ubiquitous Language Rule

The following terms must never be used interchangeably:

Subject ≠ Principal

Permission ≠ Role

Permission ≠ Policy

Role ≠ Authorization

Policy ≠ Permission

Authentication ≠ Authorization

Domain Event ≠ Integration Event

Tenant ≠ Organization

Context ≠ Policy

Usage Constraint ≠ Permission

Quota ≠ Permission

Effective Permission ≠ Permission Assignment

Using incorrect terminology is considered a domain modeling error.