# ADR-003

Title:

External Policy Engine

Status:

Accepted

---

# Context

ABAC rules become complex over time.

Embedding rules in code creates maintenance issues.

---

# Decision

Use Open Policy Agent (OPA).

Policy evaluation is externalized.

---

# Consequences

Positive:

- Flexible policies
    
- Dynamic authorization
    
- Language independence
    

Negative:

- Rego learning curve
    
- Additional infrastructure