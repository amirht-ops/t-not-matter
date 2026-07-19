# ADR-007: Department and Membership Ownership

## Status
Proposed

## Context
The system currently has the `Department` concept implemented as an aggregate root in both `TenantService` and `AuthorizationService`. Additionally, `IdentityService` manages the association between Users and Departments. This duplication has led to split ownership, data inconsistency risks, and architectural smells where security boundaries are blurred with organizational boundaries.

## Problem Statement
The duplicated `Department` aggregate in `AuthorizationService` creates a "False Ownership" scenario where authorization logic attempts to manage department members, while the actual organizational link is managed in `IdentityService`. This violates the DRY (Don't Repeat Yourself) principle at the architectural level and complicates the ubiquitous language of the system.

## Decision
We will centralize the ownership of organizational concepts and separate them from identity and security concerns:

1.  **TenantService** is the single Source of Truth (SoT) for `Department` (lifecycle, identity, and metadata).
2.  **IdentityService** is the single Source of Truth for the **User → Department** relationship (Membership).
3.  **Membership** is defined as an **Organizational** concept, not a Security concept.
4.  **Constraints**:
    *   Every User belongs to exactly one Tenant.
    *   Every User belongs to exactly one Department.
5.  **AuthorizationService** will no longer own or store a `Department` aggregate. It will treat `DepartmentId` as an opaque, external runtime reference passed via request context or claims.
6.  **IdentityService** will store only the `DepartmentId` (Guid), never Department metadata (Name, Description).
7.  **Roles and Permissions** remain exclusively owned by `AuthorizationService`.
8.  **Data Duplication**: Organizational metadata must never be duplicated across bounded contexts.

## Ownership Matrix

| Concept | Source of Truth | Primary Responsibilities |
| :--- | :--- | :--- |
| **Tenant** | TenantService | Lifecycle, Plan/Billing, Isolation Boundary |
| **Department** | TenantService | Org Hierarchy, Name, Description, Status |
| **User** | IdentityService | Credentials, Profile, Account Status |
| **Membership** | IdentityService | Association of User to Department |
| **Role** | AuthorizationService | Permission Grouping, Hierarchy |
| **Permission** | AuthorizationService | Action + Resource definitions |

## Context Boundaries
*   **Tenant Context**: Manages the "Where" (Organizational structure).
*   **Identity Context**: Manages the "Who" (User identity and their place in the structure).
*   **Authorization Context**: Manages the "What" (Permissions and roles based on Who and Where).

## Consequences
*   **Positive**: Clearer ownership, reduced database footprint in `AuthorizationService`, eliminated risk of metadata drift.
*   **Negative**: `AuthorizationService` can no longer perform server-side joins with Department names; policies must rely on IDs or have metadata passed in the context.
*   **Neutral**: Increased reliance on integration events (e.g., `UserDepartmentChanged`) for downstream cache invalidation.

## Alternatives Considered
*   **Shared Kernel**: Placing Department in a shared library. Rejected as it couples the deployment of all services and violates service autonomy.
*   **Security Groups**: Introducing a new aggregate for security-based grouping. Rejected to avoid over-engineering; the existing Department structure is sufficient for current organizational requirements.

## Migration Strategy
A multi-phase "Extract and Delete" strategy:
1.  **Phase 1**: Update `AuthorizationService` to accept `DepartmentId` as a pure reference.
2.  **Phase 2**: Remove `Department` Aggregate and Repository from `AuthorizationService`.
3.  **Phase 3**: Clean up `IdentityService` to ensure it only tracks ID references.

## Future Work
*   Implement a caching mechanism in `AuthorizationService` if Department metadata is frequently required for policy evaluation without wanting to pass it in every request.
*   Formalize `UserDepartmentChanged` integration event to ensure authorization decisions are always based on the most recent organizational context.
