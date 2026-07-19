# ADR-002

Title:

Event Driven Synchronization

Status:

Accepted

---

# Context

Authorization data changes frequently.

Caches require invalidation.

---

# Decision

Use RabbitMQ for event distribution.

Events:

RoleUpdated

PermissionUpdated

PolicyUpdated

UserDisabled

TenantUpdated

---

# Consequences

Positive:

- Loose coupling
    
- Scalability
    

Negative:

- Eventual consistency
    
- Message management complexity