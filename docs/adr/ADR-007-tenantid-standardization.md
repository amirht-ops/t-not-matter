# ADR-007

Title:

TenantId ValueObject Standardization

Status:

Accepted

---

# Context

The codebase had inconsistent TenantId representations across bounded contexts:

1. **TenantService.Domain.ValueObjects.TenantId** — A string-based ValueObject wrapping `tnnt_xxx` prefixed identifiers (business-facing tenant codes).

2. **AuthorizationService.Domain.ValueObjects.TenantId** — A Guid-based ValueObject that duplicated `Entity.TenantId` (inherited from `AggregateRoot` / `Entity` base class in SharedKernel).

This inconsistency created confusion about which TenantId to use in cross-service communication, integration events, and API contracts.

The base class `Entity` in SharedKernel already provides `Guid TenantId` as the canonical infrastructure tenant identifier used for PostgreSQL Row-Level Security, composite primary keys, and cross-service correlation.

---

# Decision

1. **TenantService.Domain.ValueObjects.TenantId** (string) is renamed to `TenantCode`. This ValueObject represents the business-facing tenant identifier (`tnnt_xxx`) used for tenant discovery and human-readable identification. It is not a substitute for the infrastructure `TenantId`.

2. **AuthorizationService.Domain.ValueObjects.TenantId** (Guid) is removed entirely. All usages are replaced with bare `Guid` or the inherited `Entity.TenantId` property from `AggregateRoot`.

3. No new custom `TenantId` ValueObjects shall be created in any bounded context. The rule is enforced by an architecture test.

---

# Rationale

- `Guid TenantId` from `Entity` is the single source of truth for infrastructure tenant identification.
- `TenantCode` (formerly `TenantId` in TenantService) is a distinct business concept — the human-readable tenant code — and should have a distinct name.
- The `AuthorizationService.TenantId` was redundant: it wrapped `Guid` and added no behavior beyond what `Entity.TenantId` already provides.
- Eliminating the redundant VO removes ~50 lines of `.Value` dereferencing and `TenantId.From()` boilerplate across AuthorizationService.

---

# TenantId Convention

| Context | Type | Example | Purpose |
|---------|------|---------|---------|
| SharedKernel (Entity) | `Guid TenantId` | `3f8e9c2a-...` | RLS, composite keys, integration events |
| TenantService | `TenantCode Code` | `tnnt_abc...` | Business-facing tenant identifier |
| All other services | `Guid TenantId` | `3f8e9c2a-...` | Infrastructure tenant identifier (inherited from Entity) |

---

# Consequences

Positive:

- Single, consistent `Guid TenantId` across all bounded contexts.
- No confusion between business tenant code and infrastructure tenant identifier.
- Reduced boilerplate in AuthorizationService (~50 `TenantId.From()` / `.Value` calls eliminated).
- Architecture test prevents regression.

Negative:

- Breaking change for any external consumer referencing `AuthorizationService.Domain.ValueObjects.TenantId` (none known).
- Minor rename in TenantService requires coordination for any consumer storing the string `TenantId` property (now `Code`).
