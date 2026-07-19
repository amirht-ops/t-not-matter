# ADR-004

Title:

Shared Database Shared Schema

Status:

Accepted

---

# Context

System must support thousands of tenants.

---

# Decision

Use:

PostgreSQL

Shared Database

Shared Schema

TenantId on aggregates

PostgreSQL Row Level Security

---

# Consequences

Positive:

- Simplicity
    
- Lower cost
    
- Easier operations
    

Negative:

- Requires strict governance
    
- Strong testing required