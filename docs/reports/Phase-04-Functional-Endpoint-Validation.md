# Phase 4 — Functional Endpoint Validation

Status: COMPLETE — PASS
Date: 2026-07-17
Scope: Execute every discovered API endpoint for real against the running services (no mocking, no simulation), capture request/response evidence, verify behaviors (CRUD chains, action endpoints, state machines, idempotency, side effects, cross-service flows, negative/authorization tests), root-cause every failure, fix the underlying defect (not the symptom), re-verify with real HTTP calls that assert the actual decision, and record a final PASS/BLOCKED verdict.

## 1. Environment

| Service | Base URL | Notes |
| --- | --- | --- |
| IdentityService | http://localhost:5244 | Auth, users, sessions, MFA |
| TenantService | http://localhost:5068 | Tenants, departments |
| AuthorizationService | http://localhost:5108 | Permissions, roles, decisions |
| PolicyService | http://localhost:5000 | Policies, subscriptions, quotas, consumption, ledgers |

Backing infrastructure: PostgreSQL (with Row-Level Security), Redis (rate limiting + decision cache), RabbitMQ (Outbox), OPA (Open Policy Agent, http://localhost:8181).

Authentication used the seeded super-admin (`admini.super`) whose JWT carries `platform_admin=true` (permission `platform.manage`). This satisfies the `PlatformServiceOnly` / `AllowIdentityAndAuthorization` policies (see `Platform.Middleware/AuthorizationPolicyExtensions.cs`) and, in the OPA base RBAC policy, matches the platform-administrator rule.

## 2. Execution Harnesses

- `tests-http/phase4/harness.mjs` — main harness (Node.js, `fetch`, real HTTP). Runs full CRUD chains, state-machine transition checks (including illegal transitions), idempotency re-plays, cross-service consumption flows, and negative/authorization tests. Evidence per call under `tests-http/phase4/evidence/`.
- `tests-http/phase4/verify-opa-fix.mjs` — focused decision-assertion check for the four OPA-dependent endpoints (asserts the actual `isAllowed` decision, not merely HTTP 200).
- `tests-http/phase4/identity-user-actions.mjs` — paced runner for the 8 identity user-action endpoints that the main harness could not reach because the shared authentication rate limiter (10 req / 60s) was saturated by the identity-heavy tail.
- `tests-http/phase4/verify-enum-fix.mjs` — focused regression for the quota enum validation gap.

## 3. Results Summary

| Area | Endpoints | Verdict |
| --- | --- | --- |
| Auth (login, refresh) | login, refresh | PASS |
| Tenant CRUD + status/plan state machine | create, get-by-slug, change-status (+idempotent), update-plan | PASS |
| Department CRUD | create, list, get, update, delete (+idempotent) | PASS |
| Authorization | permission create, role create, grant, assign, get, change, revoke, effective-permissions, evaluate, batch, record-result | PASS |
| Authorization decision (real allow) | evaluate `isAllowed=true`, batch `isAllowed=true` | PASS |
| Policy CRUD + lifecycle state machine | create, list, get, change-condition, publish, archive (+idempotent), illegal transitions | PASS |
| Quota CRUD | define, get, amend, list-by-scope, resolve, remove (+idempotent) | PASS |
| Subscription CRUD + lifecycle state machine | assign, get, list, activate (+idempotent), resolve, revoke, supersede, illegal transitions | PASS |
| Consumption / Ledgers (cross-service) | principal edge upsert, record (+idempotent), allowance, usage ledger, debt ledger, allowance reset | PASS |
| Session logout / revoke-by-id | logout, revoke-by-id | PASS (real 200 after tenant-propagation fix) |
| Identity user lifecycle + MFA | register, get, activate, mfa enable, mfa verify, unlock, disable, delete | PASS (8/8 via paced runner) |
| Negative / authorization | missing fields, no-token, wrong password, not-found, bad identifier, bad key, invalid enum | PASS |

Every discovered endpoint executed for real and returned its expected result. Four genuine defects were discovered and fixed; all were re-verified against the running services.

## 4. Defects Found and Fixed

### 4.1 OPA base policy not provisioned (BLOCKER — fixed, decision-verified)
- Symptom: every OPA-backed decision (`decisions/evaluate`, `decisions/batch`, and the session `logout` / `revoke` actions that call OPA) failed closed to Deny, even for the super-admin.
- Root cause: OPA had no decision rule loaded at the query path `data.authorization.allow` (`OpaOptions.PolicyPath`). With no rule present, the fail-closed deny-by-default contract returned Deny for all inputs. This was a missing-provisioning defect, not a policy-logic defect.
- Fix: added `OpaPolicyProvisioningTask` (an application startup task) that loads the base RBAC policy (`OpaClient/Policies/authorization.rego`, embedded as a build resource) into OPA at startup. The policy encodes the deny-by-default model plus two allow rules: (1) platform administrator holding `platform.manage`, (2) RBAC grant match where the requested `action` is in the subject's effective permissions.
- Verification (`verify-opa-fix.mjs`, asserts the decision not just the status):
  - `decisions/evaluate` → 200 with `isAllowed:true`, `reasonMessage:"Allowed: platform administrator (platform.manage)."` PASS.
  - `decisions/batch` → 200 with first decision `isAllowed:true`. PASS.
  - Startup log confirms `Provisioned OPA base authorization policy 'authorization'.` and `GET /v1/policies` lists it.

### 4.2 Tenant not propagated on service-to-service authorization calls (BLOCKER — fixed, verified)
- Symptom: `sessions/logout` and `sessions/{id}` revoke returned 403 for the super-admin even after 4.1, despite OPA allowing the platform admin.
- Root cause: `IdentityService` calls `AuthorizationService`'s `decisions/evaluate` via `Platform.Authorization/AuthorizationServiceClient`. The client (a) placed `request.TenantId` into the `OwnerId` positional slot of `EvaluateAuthorizationRequest` (wrong field) and (b) never propagated the caller's tenant to the server. The service-to-service token carries no `tenant_id` claim (by design, 90s lifetime), so `AuthorizationService` resolved `ctx.TenantId = Guid.Empty`, found zero effective permissions in the empty tenant, and denied (fail-closed).
- Fix (`AuthorizationServiceClient.cs`): set `OwnerId` to `null`, switch from `PostAsJsonAsync` to an explicit `HttpRequestMessage`, and propagate the caller's tenant via the `X-Tenant-Id` override header when `request.TenantId != Guid.Empty`. `TenantMiddleware` honors `X-Tenant-Id` for principals with `CanBypassTenantIsolation` (platform services and super-admin qualify), so the decision is now evaluated in the subject's real tenant.
- Verification (`verify-opa-fix.mjs`): `sessions/{id}` revoke → 200; `sessions/logout` → 200. Main harness steps for logout / revoke-by-id also PASS 200.

### 4.3 EnableMfa missing the platform-admin/service authorization bypass (CONSISTENCY GAP — fixed, verified)
- Symptom: `POST /api/v1/identity/mfa/enable` returned 403 when a platform admin enabled MFA for a user in another tenant (e.g., a self-registered user in the global tenant), while the sibling user-management endpoints (activate/disable/unlock/delete) succeeded for the same actor and target.
- Root cause: `ActivateUserCommandHandler`, `DisableUserCommandHandler`, `UnlockUserCommandHandler`, and `DeleteUserCommandHandler` all guard their per-action OPA check with `if (!context.CanBypassTenantIsolation && !context.IsService)`, letting a platform admin or trusted service principal act across tenants. `EnableMfaCommandHandler` lacked that guard, so it always ran the OPA check evaluating the actor's permissions in the *target* tenant — where a cross-tenant platform admin holds no grant — and denied. This is an inconsistent authorization surface: an admin who can manage a user's full lifecycle could not enable that user's MFA.
- Fix (`EnableMfaCommandHandler.cs`): wrapped the OPA decision in the same `if (!context.CanBypassTenantIsolation && !context.IsService)` bypass used by the sibling handlers, preserving the OPA check for ordinary tenant users while allowing platform/service principals.
- Verification (`identity-user-actions.mjs`): `mfa/enable` → 200, `mfa/verify` → 200 (real TOTP verified: `verified:true`) for a platform-admin-managed user. Rebuilt IdentityService (0 errors), restarted, re-ran.

### 4.4 PolicyService JWT signing-key mismatch (BLOCKER — fixed)
- Symptom: every authenticated PolicyService endpoint returned 401.
- Root cause: `PolicyService.Api/appsettings.json` shipped a placeholder `Jwt:SigningKey` that did not match the key IdentityService signs tokens with. PolicyService rejected otherwise-valid tokens.
- Fix: added the shared development key via `PolicyService.Api/appsettings.Development.json`. Rebuilt and restarted PolicyService; all PolicyService endpoints then returned expected 2xx.
- Verification: policy, quota, subscription, consumption, and ledger steps all PASS.

### 4.5 Quota define accepts undefined enum values (VALIDATION GAP — fixed)
- Symptom: `POST /api/v1/quotas` with `scopeKind=99` returned 201 Created instead of 400.
- Root cause: neither `DefineQuotaPolicyCommandValidator` nor the domain `SubscriptionScope.Create` validated that `ScopeKind` is a defined `PrincipalKind`. An out-of-range enum passed straight through to persistence.
- Fix (defense-in-depth, two layers): `DefineQuotaPolicyCommandValidator` added `RuleFor(x => x.ScopeKind).IsInEnum();`; `SubscriptionScope.Create` added `if (!Enum.IsDefined(kind)) return Failure(PolicyErrors.ScopeKindInvalid);`; added `PolicyErrors.ScopeKindInvalid`.
- Verification (`verify-enum-fix.mjs`): invalid enum (`scopeKind=99`) → 400 with `ScopeKind` validation error; valid enum (`scopeKind=3`) → 201 (no regression).

## 5. Rate-Limit Sequencing (resolved, not a defect)

- The IdentityService `AuthenticationLimiter` (10 requests / 60s fixed window) is shared across the login and register endpoints. When the main harness reached its identity-heavy tail, the window was saturated and `register` returned 429, which also skipped the 8 downstream identity user-action endpoints.
- This is an environmental test-sequencing artifact, not a product defect. It was resolved for validation purposes by extracting the identity block into `identity-user-actions.mjs`, which paces each auth-limited call with a 429 back-off. All 8 identity endpoints then executed for real and passed.

## 6. Behaviors Verified

- CRUD chains: create → read → update → delete verified for tenants, departments, policies, quotas, subscriptions, permissions/roles.
- State machines:
  - Policy: Draft → (change-condition) → Publish; change-condition after publish rejected (400); re-publish rejected (400); archive idempotent. PASS.
  - Subscription: Assign → Activate (idempotent) → Revoke; activate-after-revoke rejected (400); supersede on a fresh subscription. PASS.
  - Tenant: change-status idempotent; plan update. PASS.
  - User: Pending → (Activate) → Active → (Disable) → Disabled → (Delete) → Deleted; MFA enable requires Active (Pending → 409 `UserMustBeActive`, correct); Unlock requires Locked (Active → 409, correct). PASS.
- Authorization decisions: real `isAllowed:true` asserted for the platform admin on evaluate and batch (not merely HTTP 200); session logout/revoke allowed by OPA after tenant propagation.
- Idempotency: repeated status change, delete, archive, activate, and consumption record (same idempotency key) all returned stable results. PASS.
- Side effects / cross-service: consumption resolves an effective subscription AND quota across the `[User(consumer), Tenant]` scope chain; allowance and usage/debt ledgers reflected the recorded consumption. PASS.
- Negative / authorization: missing-field 400, no-token 401, wrong-password 401, not-found 404, bad-identifier 400, bad-key 400, and invalid-enum 400. PASS.

## 7. Files Changed

- `src/Services/AuthorizationService/AuthorizationService.Infrastructure/OpaClient/OpaPolicyProvisioningTask.cs` — startup task provisioning the base RBAC policy to OPA (fix 4.1).
- `src/Services/AuthorizationService/AuthorizationService.Infrastructure/OpaClient/Policies/authorization.rego` — embedded base RBAC decision policy (fix 4.1).
- `src/Platform/Platform.Authorization/AuthorizationServiceClient.cs` — propagate caller tenant via `X-Tenant-Id`; stop polluting the `OwnerId` slot (fix 4.2).
- `src/Services/IdentityService/IdentityService.Application/Features/EnableMfa/EnableMfaCommandHandler.cs` — platform-admin/service OPA bypass to match sibling handlers (fix 4.3).
- `src/Services/PolicyService/PolicyService.Api/appsettings.Development.json` — shared dev `Jwt:SigningKey` (fix 4.4).
- `src/Services/PolicyService/PolicyService.Application/Features/QuotaPolicies/DefineQuotaPolicy.cs` — `ScopeKind` `IsInEnum()` validation (fix 4.5).
- `src/Services/PolicyService/PolicyService.Domain/ValueObjects/SubscriptionScope.cs` — `Enum.IsDefined(kind)` guard (fix 4.5).
- `src/Services/PolicyService/PolicyService.Domain/Errors/PolicyErrors.cs` — `ScopeKindInvalid` error (fix 4.5).
- `tests-http/phase4/harness.mjs`, `verify-opa-fix.mjs`, `identity-user-actions.mjs`, `verify-enum-fix.mjs` — validation harnesses and focused regression checks.

## 8. Verdict

PASS. Every discovered API endpoint was executed for real against the running services and returned its expected behavior. Four genuine defects were discovered through real execution, root-caused, fixed at the source (not patched at the symptom), and re-verified with real HTTP calls that assert the actual outcome:

1. OPA base policy not provisioned (all decisions failing closed) — fixed and decision-verified.
2. Tenant not propagated on service-to-service authorization calls (logout/revoke 403) — fixed and verified 200.
3. EnableMfa missing the platform-admin/service bypass (cross-tenant MFA 403) — fixed and verified 200 with real TOTP.
4. PolicyService JWT signing-key mismatch (401) and quota enum validation gap (201 → 400) — fixed and verified.

No BLOCKED items remain. The register 429 was an environmental rate-limit sequencing artifact and was resolved for validation by pacing the identity block; all 8 identity user-action endpoints subsequently passed (8/8).
