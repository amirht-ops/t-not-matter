# Project Context

Version: 3.0

Status: Source Of Truth

Purpose:

This document provides the mental model of the Enterprise Authorization Platform.

Before generating code, architecture, tests, events, APIs, or documentation, AI agents and developers must read this document.

This document explains:

- Why the platform exists
    
- What problem it solves
    
- How the domain works
    
- What concepts are central
    
- What assumptions are forbidden
    

This document is not an implementation guide.

It is a domain and architectural context guide.

---

# Mission

The mission of this platform is to become the authoritative decision engine for authorization across an enterprise ecosystem.

The platform answers a single question:

Can a Subject perform an Action on a Resource under a given Context?

Everything else exists to support this decision.

---

# Vision

Authorization must not be distributed across business services.

Authorization must not be hardcoded inside applications.

Authorization must not depend on local implementation decisions.

Instead:

All authorization decisions are centralized.

All authorization decisions are auditable.

All authorization decisions are explainable.

All authorization decisions are policy-driven.

---

# What This Platform Is

This platform is:

- An Authorization Platform
    
- A Policy Decision Platform
    
- A Multi-Tenant Security Platform
    
- A Permission Management Platform
    
- A Delegation Platform
    
- A Usage-Constrained Access Platform
    

The platform is not merely RBAC.

The platform combines:

- RBAC
    
- ABAC
    
- Delegation
    
- Usage Constraints
    
- Resource Constraints
    

into a unified authorization model.

---

# What This Platform Is Not

The platform is not:

- An API Gateway
    
- An Identity Provider
    
- A Workflow Engine
    
- A Business Process Engine
    
- A User Management System
    
- A Reporting Platform
    

The platform may integrate with those systems.

It does not replace them.

---

# Core Question

Every capability ultimately serves:

Can Subject perform Action on Resource under Context?

This question is the center of the domain.

Everything else is supporting infrastructure.

---

# Core Decision Model

Authorization decisions are evaluated using:

Subject  
+  
Action  
+  
Resource  
+  
Context  
+  
Policies

Result:

Allow  
or  
Deny

Authorization decisions are never based solely on:

- Roles
    
- Claims
    
- Groups
    

These are inputs.

Not decisions.

---

# Core Philosophy

The platform follows:

Permission-Based Authorization

not

Role-Based Authorization

Roles exist to simplify permission assignment.

Permissions determine capability.

Policies determine applicability.

Context determines eligibility.

Usage determines availability.

The final decision emerges from evaluation.

---

# Why Traditional RBAC Is Not Enough

Traditional RBAC assumes:

Role  
→ Permission  
→ Allow

The platform rejects this model.

Examples:

A supervisor may:

- Create invoices 100 times per month
    
- Edit an invoice once per day
    
- Read the same invoice three times per day
    

Traditional RBAC cannot express these constraints.

The platform extends RBAC through:

- Policies
    
- Runtime Context
    
- Usage Constraints
    
- Resource Constraints
    

---

# Authorization as a Platform Capability

Authorization is not an implementation detail.

Authorization is a platform capability.

Business systems should ask for decisions.

Business systems should not make decisions.

Examples:

Warehouse Service

Billing Service

ERP Service

CRM Service

must all request authorization from this platform.

---

# Centralized Decision Making

The platform is the single source of truth for authorization decisions.

Business services may provide context.

Business services may not:

- Evaluate permissions
    
- Evaluate roles
    
- Evaluate policies
    
- Evaluate quotas
    
- Evaluate delegation rules
    

Authorization logic belongs only to the platform.

---

# Multi-Tenant First

Multi-tenancy is not an additional feature.

Multi-tenancy is a foundational design constraint.

Every concept exists inside a tenant boundary.

Examples:

Users

Roles

Permissions

Policies

Delegations

Authorization Decisions

All are tenant scoped.

---

# Tenant Boundary

A tenant represents a security boundary.

Cross-tenant access is denied by default.

Tenant isolation takes precedence over convenience.

A tenant boundary violation is considered a critical security incident.

---

# Stateful Authorization

Most authorization systems are stateless.

This platform is not.

Authorization decisions may depend on historical usage.

Examples:

How many times did the user perform this action today?

How many times was this resource accessed this month?

How many edits already occurred?

These questions influence authorization outcomes.

---

# Runtime Context

Authorization requires runtime context.

Examples:

Tenant

Subject

Environment

Resource

Current Time

Usage Counters

Resource Access Counts

Risk Signals

The same permission may produce different decisions under different contexts.

---

# Policy Driven Authorization

Policies are first-class domain concepts.

Policies determine whether permissions can be exercised.

Policies evaluate runtime context.

Policies are externalized.

Policies are not embedded inside application code.

---

# OPA Strategy

OPA is the policy evaluation engine.

OPA evaluates:

- ABAC Rules
    
- Contextual Constraints
    
- Dynamic Restrictions
    
- Usage Rules
    

Services never duplicate policy logic.

OPA is the authoritative policy evaluator.

---

# Delegation Model

Delegation allows temporary transfer of authority.

Delegation is:

- Explicit
    
- Auditable
    
- Time-Bound
    
- Revocable
    

Delegation does not transfer ownership.

Delegation transfers authority.

---

# Usage-Constrained Access

Permissions do not automatically guarantee access.

Access may be limited by usage constraints.

Examples:

Daily Limits

Monthly Limits

Per Resource Limits

Per Action Limits

Usage constraints participate in authorization decisions.

---

# Resource-Centric Authorization

Authorization is evaluated against resources.

A resource may be:

- A Type
    
- An Instance
    

Examples:

invoice.read

or

invoice-123.read

Resource-specific constraints are first-class concepts.

---

# Auditability

Every authorization decision must be explainable.

Every authorization decision must be traceable.

Every authorization decision must be auditable.

Security decisions without auditability are unacceptable.

---

# Reliability Model

Authorization correctness is more important than availability.

When uncertainty exists:

Deny

The platform follows:

Fail Closed

never

Fail Open

---

# Event Driven Platform

The platform is event-driven.

Events are used for:

- Synchronization
    
- Audit
    
- Usage Accounting
    
- Cache Invalidation
    
- Cross-Service Communication
    

Events are domain concepts.

Not implementation details.

---

# Bounded Contexts

Current bounded contexts:

Identity

Authorization

Policy

Delegation

Tenant

Audit

Each bounded context owns:

- Domain Model
    
- Data
    
- APIs
    
- Events
    

No shared database ownership exists.

---

# Source Of Truth Strategy

Authorization Decisions

→ Authorization Platform

Policy Evaluation

→ OPA

Persistent State

→ PostgreSQL

Messaging

→ RabbitMQ

Caching

→ Redis

Observability

→ OpenTelemetry

Each responsibility has one owner.

---

# Security Philosophy

Security takes precedence over:

- Convenience
    
- Performance
    
- Simplicity
    

The platform follows:

Least Privilege

Fail Closed

Tenant Isolation

Explicit Authorization

Defense In Depth

Auditability

---

# AI Agent Guidance

Before generating code read:

1. project-context.md
    
2. architecture-rules.md
    
3. coding-rules.md
    
4. domain-glossary.md
    
5. ADRs
    
6. Specifications
    

AI agents must never assume:

- Permissions
    
- Policies
    
- Tenant Rules
    
- Delegation Rules
    
- Usage Constraints
    
- Event Contracts
    

When information is missing:

Ask.

Do not invent.

---

# Mental Model Summary

Think about the platform as:

A centralized, multi-tenant, policy-driven authorization platform that evaluates whether a subject may perform an action on a resource under a given context while considering permissions, policies, delegation, usage constraints, and tenant boundaries.

Everything else is implementation detail.