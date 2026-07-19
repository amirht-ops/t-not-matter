# ADR-008: Identity Context Redesign and Aggregate Splitting

## Status
Proposed

## Context
Following the completion of ADR-007, which centralized Department ownership in the Tenant context and Membership in the Identity context, we have identified a significant architectural bottleneck. The current `User` aggregate in `IdentityService` is a "God Aggregate" that conflates three distinct lifecycles:
1.  **Identity** (Who is the person? What is their status? Where do they belong?)
2.  **Authentication** (How do they prove who they are? What are their secrets?)
3.  **Security/Risk** (Is this request safe? Are they under attack?)

This conflation results in high write contention on the `User` table during every login attempt (successful or failed) and session refresh, caused by updating metadata and version counters on a single aggregate root.

## Problem Statement
The current `User` aggregate enforces artificial consistency boundaries. For example, a failed login attempt (volatile security telemetry) increments the version of the entire User record, potentially blocking a concurrent legitimate password change or administrative department move. We need a model that preserves strict invariants where necessary but allows high-frequency authentication operations to scale without contention.

## Candidate Architectures

### Option A: Status Quo (Monolithic User)
*   **Description**: Keep all fields (Credentials, MFA, FailedAttempts, Status) in `User`.
*   **Evaluation**: Fails on scalability. Violates the principle of Small Aggregates (Vernon). High risk of `DbUpdateConcurrencyException`.

### Option B: The "Session Split" (Current trajectory)
*   **Description**: Split `Session` into a separate aggregate. Keep `Credential` and `SecurityState` (FailedAttempts) in `User`.
*   **Evaluation**: Solves session refresh contention but leaves the "Login Hotspot." Every login still locks the `User` record to reset/increment `FailedLoginAttempts`.

### Option C: The "Tri-Aggregate" Model (Identity, Session, SecurityState)
*   **Description**: `User` (Identity + Credentials), `Session` (Access), and `SecurityState` (Risk metrics) as separate boundaries.
*   **Evaluation**: Strong balance. Identity and Credentials often share an owner (the User) and a lifecycle (Credential setup is part of Identity onboarding).

### Option D: The "Fine-Grained" Model (Identity, Credential, Session, SecurityState)
*   **Description**: Complete separation of all concepts into 4+ aggregates.
*   **Evaluation**: Maximum scalability, but high complexity. Requires eventual consistency for simple tasks like "Password Reset" (which affects both the Credential and the Identity status).

---

## Decision: Option C+ (The "Identity-Security" Split)

We will implement a hybrid of Option C, specifically designed to eliminate write hotspots while maintaining strict invariants.

### 1. The Identity Aggregate (`User`)
*   **Status**: Aggregate Root.
*   **Responsibility**: Core identity and lifecycle.
*   **Invariants**: Unique Username, Identity Status Transitions (e.g., cannot Activate a Deleted user).
*   **Includes**: `Credential` (Entity) and `MfaSettings` (Entity).
*   **Justification for Credential as Entity**: A Credential has no meaning without an Identity. The invariant "A user must have exactly one active password" is a structural identity requirement. Password changes are low-frequency enough not to warrant a separate aggregate.

### 2. The Session Aggregate (`Session`)
*   **Status**: Aggregate Root.
*   **Responsibility**: Temporal access grant and rotation.
*   **Invariants**: Token rotation (prevents replay), Expiration.
*   **Relationship**: 1:N with User.

### 3. The Security Watchdog (`AccessRisk`)
*   **Status**: **Separate Persistence Boundary** (Value Object or Entity in a separate table/store).
*   **Responsibility**: High-frequency tracking of `FailedLoginAttempts`.
*   **Decision**: This is **Security Telemetry**, not Identity. It does NOT use optimistic concurrency (no `Version` check).
*   **Lockout Logic**: When a threshold is reached, it triggers a command to the `User` aggregate to transition `Status` to `Locked`.

---

## Responsibility Matrix

| Responsibility | Aggregate | Classification | Freq |
| :--- | :--- | :--- | :--- |
| Lifecycle (Activate/Delete) | `User` | Identity | Very Low |
| Department Assignment | `User` | Organizational | Low |
| Password/MFA Update | `User` | Authentication | Low |
| Token Rotation | `Session` | Access | High |
| Failed Login Tracking | `AccessRisk` | Security | Very High |
| Account Lockout (State) | `User` | Identity/Security | Low |

## Invariant Matrix

| Invariant | Owner | Type |
| :--- | :--- | :--- |
| "A Deleted user cannot log in" | `User` | Strict |
| "A Locked user cannot log in" | `User` | Strict |
| "A Session must have a valid Token" | `Session` | Strict |
| "5 Failures result in a Lockout" | `AuthPolicy` | Eventual |

## Consistency Boundary Analysis
*   **Strict (Transactional)**: Identity Status + Credentials.
*   **Strict (Transactional)**: Session Token + Expiry.
*   **Eventual**: Failed Attempts -> Identity Lockout. We accept a millisecond window where a 6th attempt might occur before the Status is updated.

---

## Trade-off Analysis
*   **Domain Correctness**: High. Separates "Who you are" from "How many times you failed."
*   **Scalability**: Excellent. 99% of login attempts no longer trigger a version-check update on the `User` aggregate.
*   **Complexity**: Medium. Requires a new table for `AccessRisk` and a background/service logic to handle the transition to `Locked`.

---

## Migration Strategy
1.  **Step 1**: Extract `FailedLoginAttempts` from `User` aggregate into a sidecar `AccessRisk` table.
2.  **Step 2**: Update `LoginCommandHandler` to read/write `AccessRisk` without locking the `User` row.
3.  **Step 3**: Implement `User.Lockout()` command to be called only when threshold is hit.

## Amendment 1 — Aggregate Independence

`AccessRisk` is an independent Aggregate Root. It is responsible only for authentication risk telemetry and owns `FailedCount` and `LastFailureAt`. It does **not** own lock state, user status, or authentication decisions. It must never reference the `User` aggregate or mutate it. Its behavior is limited to `RecordFailure()`, `Reset()`, and exposing state for evaluation.

## Amendment 2 — AuthenticationPolicy Purity

`AuthenticationPolicy` is a pure Domain Service containing business rules only. It must never depend on infrastructure (Repositories, DB, Cache). It receives domain objects and returns business decisions (e.g., `ShouldLock(AccessRisk)`).

## Amendment 3 — Application Service Responsibilities

`LoginCommandHandler` (Application Service) is an orchestrator. It loads aggregates, invokes domain behavior, and manages transactions. It must **not** contain business rules (e.g., hardcoded thresholds). It must delegate decisions to the `AuthenticationPolicy`.

## Final Recommendation
Implement **Option C+**. This model respects the business fact that **Security Risk** is a dynamic evaluation, while **Identity** is a stable state. It preserves the `Credential` inside `User` to maintain the integrity of the authentication contract while offloading the high-frequency telemetry that causes the current hotspot.

