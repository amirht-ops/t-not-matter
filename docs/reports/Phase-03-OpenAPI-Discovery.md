# Phase 3 — OpenAPI Discovery & Test Planning

Source of truth: the live OpenAPI documents served by each service. No controller/endpoint
source files were used to build these lists. All 4 documents were retrieved over HTTP and parsed.

Captured documents (raw, saved at repo root for the following phases):
`identity_openapi.json`, `authz_openapi.json`, `policy_openapi.json`, `tenant_openapi.json`.

---

## 1. OpenAPI Status

| Service | OpenAPI URL | HTTP | OpenAPI ver | Title | Operations |
|---|---|---|---|---|---|
| IdentityService | http://localhost:5244/openapi/v1.json | 200 | 3.1.1 | IdentityService.Api \| v1 | 14 |
| AuthorizationService | http://localhost:5108/openapi/v1.json | 200 | 3.1.1 | AuthorizationService.Api \| v1 | 13 |
| PolicyService | http://localhost:5000/openapi/v1.json | 200 | 3.1.1 | PolicyService.Api \| v1 | 27 |
| TenantService | http://localhost:5068/openapi/v1.json | 200 | 3.1.1 | TenantService.Api \| v1 | 14 |

Total discovered operations: **68**.

Notes:
- All four services use the built-in `Microsoft.AspNetCore.OpenApi` (10.0.9) generator, not Swashbuckle/NSwag.
- OpenAPI + Scalar are mapped only when `ASPNETCORE_ENVIRONMENT=Development`.
- IdentityService was not running at the start of this phase. It failed a first launch because
  `JwtOptions.SigningKey` is only present in `appsettings.Development.json`; it was started with the
  `http` launch profile (which sets the Development environment) and then served OpenAPI with 200.
  Documented here so the following phases always start Identity in Development.

Result: **OpenAPI successfully generated for all 4 services.**

## 2. Scalar Status

| Service | Scalar URL | HTTP |
|---|---|---|
| IdentityService | http://localhost:5244/scalar/v1 | 200 |
| AuthorizationService | http://localhost:5108/scalar/v1 | 200 |
| PolicyService | http://localhost:5000/scalar/v1 | 200 |
| TenantService | http://localhost:5068/scalar/v1 | 200 |

Documents load, operations/schemas/security definitions are present in each JSON. Result: **Scalar available.**

---

## 3. Endpoint Inventory

Every operation appears exactly once. `Consumes`/`Produces` are `application/json` unless noted.
Security column shows what the OpenAPI document declares (see the caveat in Section 6).

Authorization column values: `Doc` = what the OpenAPI document declares (uniformly `TenantHeader + Bearer`
on every operation, per the transformer). `Effective` = the requirement inferred from operation semantics
(the sound reading — see Section 5 caveat).

### IdentityService (base `/`, port 5244)

| Method | Route | Operation | Authorization | Consumes | Produces | Status Codes | Tag |
|---|---|---|---|---|---|---|---|
| GET | /health/live | Liveness | Anonymous | – | json | 200 | Health |
| GET | /health/ready | Readiness | Anonymous | – | – | 200 | Health |
| POST | /api/v1/identity/auth/register | (register) | Anonymous | json | – | 200 | Identity Auth |
| POST | /api/v1/identity/auth/login | (login) | Anonymous | json | – | 200 | Identity Auth |
| POST | /api/v1/identity/auth/refresh | (refresh) | Anonymous (refresh token) | json | – | 200 | Identity Auth |
| POST | /api/v1/identity/sessions/logout | (logout) | Bearer | json (nullable) | – | 200 | Identity Sessions |
| DELETE | /api/v1/identity/sessions/{id} | (revoke session) | Bearer | – | – | 200 | Identity Sessions |
| POST | /api/v1/identity/mfa/verify | (mfa verify) | Bearer | json | – | 200 | Identity Sessions |
| POST | /api/v1/identity/mfa/enable | (mfa enable) | Bearer | json | – | 200 | Identity Sessions |
| POST | /api/v1/identity/users/{userId}/disable | (disable user) | Platform/Admin | – (+ query targetTenantId) | – | 200 | Identity Sessions |
| POST | /api/v1/identity/users/{userId}/activate | (activate user) | Platform/Admin | – (+ query targetTenantId) | – | 200 | Identity Sessions |
| POST | /api/v1/identity/users/{userId}/unlock | (unlock user) | Platform/Admin | – (+ query targetTenantId) | – | 200 | Identity Sessions |
| DELETE | /api/v1/identity/users/{userId} | (delete user) | Platform/Admin | – (+ query targetTenantId) | – | 200 | Identity Sessions |
| GET | /api/v1/identity/users/{userId} | (get user) | Bearer | – | – | 200 | Identity Users |

### AuthorizationService (base `/`, port 5108)

| Method | Route | Operation | Authorization | Consumes | Produces | Status Codes | Tag |
|---|---|---|---|---|---|---|---|
| GET | /health/live | Liveness | Anonymous | – | json | 200 | Health |
| GET | /health/ready | Readiness | Anonymous | – | – | 200 | Health |
| POST | /api/v1/authorization/decisions/evaluate | (evaluate) | Platform/Service | json | – | 200 | Authorization Decisions |
| POST | /api/v1/authorization/decisions/batch | (batch evaluate) | Platform/Service | json | – | 200 | Authorization Decisions |
| GET | /api/v1/authorization/decisions/subjects/{subjectId}/effective-permissions | (effective perms) | Bearer + Tenant | – | – | 200 | Authorization Decisions |
| POST | /api/v1/authorization/decisions/record-result | (record result) | Platform/Service | json | – | 200 | Authorization Decisions |
| POST | /api/v1/authorization/roles | (create role) | Bearer + Tenant | json | – | 200 | Authorization Roles |
| POST | /api/v1/authorization/roles/assignments | (assign role) | Bearer + Tenant | json | – | 200 | Authorization Roles |
| POST | /api/v1/authorization/roles/assignments/revoke | (revoke role) | Bearer + Tenant | json | – | 200 | Authorization Roles |
| POST | /api/v1/authorization/roles/assignments/change | (change role) | Bearer + Tenant | json | – | 200 | Authorization Roles |
| GET | /api/v1/authorization/roles/assignment | (get assignment) | Bearer + Tenant | – (+ query subjectId) | – | 200 | Authorization Roles |
| POST | /api/v1/authorization/permissions | (create permission) | Platform/Admin | json | – | 200 | Authorization Permissions |
| POST | /api/v1/authorization/permissions/grants | (grant permission) | Platform/Admin | json | – | 200 | Authorization Permissions |

### TenantService (base `/`, port 5068)

| Method | Route | Operation | Authorization | Consumes | Produces | Status Codes | Tag |
|---|---|---|---|---|---|---|---|
| GET | /health/live | Liveness | Anonymous | – | json | 200 | Health |
| GET | /health/ready | Readiness | Anonymous | – | – | 200 | Health |
| POST | /api/v1/tenants | CreateTenant | Platform/Admin | json | json | 201,400,409 | Tenant Management |
| PATCH | /api/v1/tenants/{id}/status | ChangeTenantStatus | Platform/Admin | json | json | 200,400,404,403 | Tenant Management |
| PATCH | /api/v1/tenants/{id}/plan | UpdateTenantPlan | Platform/Admin | json | json | 200,400,404,403 | Tenant Management |
| GET | /api/v1/tenants/slug/{slug} | GetTenantBySlug | Anonymous (rate-limited) | – | json | 200,401,404,429 | Tenant Management |
| POST | /api/v1/tenants/{tenantId}/departments | CreateDepartment | Bearer + Tenant | json | json | 201,400,409 | Department Management |
| GET | /api/v1/tenants/{tenantId}/departments | ListDepartments | Bearer + Tenant | – | json | 200 | Department Management |
| GET | /api/v1/tenants/{tenantId}/departments/{id} | GetDepartmentById | Bearer + Tenant | – | json | 200,404 | Department Management |
| PATCH | /api/v1/tenants/{tenantId}/departments/{id} | UpdateDepartment | Bearer + Tenant | json | json | 200,404,400 | Department Management |
| DELETE | /api/v1/tenants/{tenantId}/departments/{id} | DeleteDepartment | Bearer + Tenant | – | – | 204,404 | Department Management |
| GET | /api/v1/tenants/me/tenant | GetMyTenant | Bearer | – | json | 200,401,404 | Tenant Profile (User) |
| GET | /api/v1/tenants/profile | GetTenantProfile | Bearer | – | json | 200,401,404 | Tenant Profile (User) |
| PUT | /api/v1/tenants/settings | UpdateTenantSettings | Bearer + Tenant | json | json | 200,400,401,404 | Tenant Profile (User) |

### PolicyService (base `/`, port 5000)

| Method | Route | Operation | Authorization | Consumes | Produces | Status Codes | Tag |
|---|---|---|---|---|---|---|---|
| GET | /health/live | Liveness | Anonymous | – | json | 200 | Health |
| GET | /health/ready | Readiness | Anonymous | – | – | 200 | Health |
| POST | /api/v1/policies | CreatePolicy | Bearer + Tenant | json | json | 201,400,409 | Policies |
| GET | /api/v1/policies | ListPolicies | Bearer + Tenant | – | json | 200,401 | Policies |
| GET | /api/v1/policies/{id} | GetPolicyById | Bearer + Tenant | – | json | 200,404 | Policies |
| POST | /api/v1/policies/{id}/publish | PublishPolicy | Bearer + Tenant | – | json | 200,404,400 | Policies |
| POST | /api/v1/policies/{id}/archive | ArchivePolicy | Bearer + Tenant | – | json | 200,404,400 | Policies |
| PATCH | /api/v1/policies/{id}/condition | ChangePolicyCondition | Bearer + Tenant | json | json | 200,404,400 | Policies |
| POST | /api/v1/quotas | DefineQuotaPolicy | Bearer + Tenant | json | json | 201,400,409 | Quota Policies |
| GET | /api/v1/quotas/{id} | GetQuotaPolicyById | Bearer + Tenant | – | json | 200,404 | Quota Policies |
| PATCH | /api/v1/quotas/{id} | AmendQuotaPolicy | Bearer + Tenant | json | json | 200,404,400 | Quota Policies |
| DELETE | /api/v1/quotas/{id} | RemoveQuotaPolicy | Bearer + Tenant | – | – | 204,404 | Quota Policies |
| GET | /api/v1/quotas/scope | ListQuotaPoliciesByScope | Bearer + Tenant | – (+ query scopeKind, scopePrincipalId) | json | 200,400 | Quota Policies |
| POST | /api/v1/quotas/resolve | ResolveEffectiveQuota | Bearer + Tenant | json | json | 200,400 | Quota Policies |
| POST | /api/v1/subscriptions | AssignSubscription | Bearer + Tenant | json | json | 201,400,404,409 | Subscriptions |
| GET | /api/v1/subscriptions/{id} | GetSubscriptionById | Bearer + Tenant | – | json | 200,404 | Subscriptions |
| GET | /api/v1/subscriptions/policy/{policyId} | ListSubscriptionsByPolicy | Bearer + Tenant | – | json | 200,400 | Subscriptions |
| POST | /api/v1/subscriptions/{id}/activate | ActivateSubscription | Bearer + Tenant | – | json | 200,404 | Subscriptions |
| POST | /api/v1/subscriptions/{id}/revoke | RevokeSubscription | Bearer + Tenant | – | json | 200,404 | Subscriptions |
| POST | /api/v1/subscriptions/{id}/supersede | SupersedeSubscription | Bearer + Tenant | – | json | 200,404 | Subscriptions |
| POST | /api/v1/subscriptions/resolve | ResolveEffectiveSubscription | Bearer + Tenant | json | json | 200,400 | Subscriptions |
| POST | /api/v1/consumption/record | RecordConsumption | Bearer + Tenant | json | json | 200,400,404 | Consumption |
| GET | /api/v1/consumption/{consumerId}/allowance | GetAllowanceStatus | Bearer + Tenant | – (+ query actionKey) | json | 200,400 | Consumption |
| GET | /api/v1/ledgers/usage | GetUsage | Bearer + Tenant | – (+ query consumerId, actionKey, window) | json | 200,400 | Ledgers |
| GET | /api/v1/ledgers/debt | GetDebt | Bearer + Tenant | – (+ query consumerId, actionKey?) | json | 200,400 | Ledgers |
| POST | /api/v1/administration/allowance/reset | ResetAllowance | Platform/Admin | json | json | 200,400,404 | Administration |
| POST | /api/v1/principal-hierarchy/edges | UpsertPrincipalEdge | Bearer + Tenant | json | json | 200,400 | Principal Hierarchy |

---

## 4. Endpoint Classification (by category)

| Category | Endpoints |
|---|---|
| Authentication | Identity: register, login, refresh, mfa/verify, mfa/enable |
| Sessions | Identity: logout, sessions/{id} DELETE |
| Users | Identity: users/{userId} GET/DELETE, disable, activate, unlock |
| Tenants | Tenant: create, status, plan, slug lookup, me/tenant, profile, settings |
| Departments | Tenant: departments CRUD (create/list/get/update/delete) |
| Roles | Authz: create role, assign, revoke, change, get assignment |
| Permissions | Authz: create permission, grant |
| Authorization Decisions | Authz: evaluate, batch, effective-permissions, record-result |
| Policies | Policy: create, list, get, publish, archive, change condition |
| Quota Policies | Policy: define, get, amend, remove, list by scope, resolve |
| Subscriptions | Policy: assign, get, list by policy, activate, revoke, supersede, resolve |
| Consumption | Policy: record, allowance status |
| Ledger | Policy: usage, debt |
| Administration | Policy: allowance reset |
| Principal Hierarchy | Policy: upsert edge |
| Health | All services: /health/live, /health/ready |

No Infrastructure/Internal-only operations are exposed in any document beyond Health.

---

## 5. Authentication Matrix (per OpenAPI + semantics)

Two security schemes are defined identically in every document:
- `TenantHeader` — apiKey, header `X-Tenant-Id` (GUID)
- `Bearer` — http bearer, JWT

### Caveat (important for later phases)

Each document applies a **global security requirement** (`TenantHeader` + `Bearer`) to **every**
operation via a document transformer. That means the OpenAPI document alone cannot distinguish
anonymous endpoints from protected ones — even `login` and `register` are marked as requiring a
Bearer token, which is logically impossible (no token exists before login). Therefore the
authentication grouping below is derived from operation semantics (the only sound reading), while
noting that the raw document over-declares security uniformly.

| Auth requirement | Endpoints (inferred from semantics) |
|---|---|
| Anonymous (must work without token) | Identity: register, login, refresh; all `/health/*`; Tenant slug lookup is rate-limited (429) and used pre-auth |
| Bearer token (authenticated user) | Identity: logout, sessions, mfa/*, users/*; Tenant: me/tenant, profile, settings; Policy read APIs |
| Bearer + Tenant header (tenant-scoped) | Tenant: departments/*, tenant management; Policy: policies, quotas, subscriptions, consumption, ledgers, principal-hierarchy |
| Platform/Service-to-service (elevated) | Authz: decisions/* (evaluate, batch, record-result), permissions/*; Tenant/Identity admin ops (user disable/activate/unlock/delete, tenant status/plan) |

Note: fine-grained policy names (e.g. `PlatformServiceOnly`, `PlatformAdmin`) are enforced in code
but are **not** surfaced in the OpenAPI document. This is flagged as a discovery gap; actual policy
enforcement will be observed empirically during execution phases, not assumed here.

---

## 6. Dependency Graph (execution order)

```
IdentityService: register / login
        │  (JWT + tenant context)
        ▼
TenantService: CreateTenant ──► X-Tenant-Id (tenantId)
        │
        ├──► CreateDepartment (needs tenantId)
        │
        ▼
AuthorizationService: CreatePermission ──► CreateRole (needs departmentId, optional parentRoleId)
        │                                        │
        │                                        ▼
        │                                   GrantPermission (needs roleId + permissionId)
        │                                        │
        │                                        ▼
        │                                   AssignRole (needs subjectId + roleId + assignedBy)
        │
        ▼
PolicyService: CreatePolicy ──► PublishPolicy
        │                              │
        │                              ▼
        │                      DefineQuotaPolicy (scope) ──► AssignSubscription (needs policyId)
        │                                                          │
        │                                                          ▼
        │                                                   ActivateSubscription
        │                                                          │
        ▼                                                          ▼
AuthorizationService: EvaluateAuthorization / Decisions ◄── RecordConsumption (needs consumerId, actionKey, quota)
```

Prerequisite rule: never call a dependent operation before its prerequisite entity exists
(JWT → Tenant → Department → Permission/Role → Policy → Quota → Subscription → Consumption → Decision).

---

## 7. CRUD Chains (validation scenarios)

Each chain is one scenario, executed create → read → update → list → delete where the verbs exist.

| Resource | Create | Read | Update | List | Delete/Terminal |
|---|---|---|---|---|---|
| Tenant | POST /tenants | GET /tenants/slug/{slug}, /me/tenant, /profile | PATCH /status, PATCH /plan, PUT /settings | (slug/profile) | status→Suspended (no hard delete) |
| Department | POST …/departments | GET …/departments/{id} | PATCH …/departments/{id} | GET …/departments | DELETE …/departments/{id} (deactivate) |
| Permission | POST /permissions | (via effective-permissions) | – | – | – |
| Role | POST /roles | GET /roles/assignment | POST /roles/assignments/change | – | POST /roles/assignments/revoke |
| Policy | POST /policies | GET /policies/{id} | PATCH /policies/{id}/condition | GET /policies | archive (POST /archive) |
| Quota Policy | POST /quotas | GET /quotas/{id}, /quotas/scope | PATCH /quotas/{id} | GET /quotas/scope | DELETE /quotas/{id} |
| Subscription | POST /subscriptions | GET /subscriptions/{id}, /policy/{policyId} | – | GET /policy/{policyId} | revoke / supersede |

---

## 8. Action Endpoints (state transitions — run after Create)

| Endpoint | Transition | Prerequisite |
|---|---|---|
| POST /policies/{id}/publish | Draft → Published | Policy created |
| POST /policies/{id}/archive | Published/Draft → Archived | Policy created |
| PATCH /policies/{id}/condition | change rule | Policy created (not archived) |
| POST /subscriptions/{id}/activate | Pending → Active | Subscription assigned |
| POST /subscriptions/{id}/revoke | Active → Revoked | Subscription assigned |
| POST /subscriptions/{id}/supersede | Active → Superseded | Subscription assigned |
| POST /consumption/record | record usage / decision | Consumer + quota exist |
| POST /administration/allowance/reset | reset usage + debt | Consumer with usage |
| POST /decisions/record-result | record op result | Decision context exists |
| PATCH /tenants/{id}/status | change tenant status | Tenant created |
| PATCH /tenants/{id}/plan | change plan tier | Tenant created |
| POST /identity/users/{userId}/disable | Active → Disabled | User exists |
| POST /identity/users/{userId}/activate | Disabled → Active | User exists |
| POST /identity/users/{userId}/unlock | Locked → Active | User exists/locked |
| POST /identity/mfa/enable | enable MFA | User exists |
| POST /identity/mfa/verify | verify MFA | MFA challenge issued |
| POST /roles/assignments/revoke | revoke role | Role assigned |
| POST /roles/assignments/change | change role | Role assigned |
| POST /principal-hierarchy/edges | upsert node/edge | Node/User/Role exist |

---

## 9. Required Test Data

Derived only from OpenAPI request schemas and `required` fields (no hardcoded values that violate schema).

| Entity | Source operation | Required fields (from schema) |
|---|---|---|
| User (registration) | RegisterRequest | userName, phoneNumber, password, email |
| Login | LoginRequest | identifier or userName, password, mfaCode (nullable) |
| Tenant | CreateTenantRequest | name, identifier, slug (planTier optional int, default 1) |
| Department | CreateDepartmentRequest | name, description (nullable) |
| Permission | CreatePermissionRequest | key, action, resourceType, description (nullable) |
| Role | CreateRoleRequest | name, description (nullable), parentRoleId (nullable uuid), departmentId (uuid) |
| Role assignment | AssignRoleRequest | subjectId, roleId, assignedBy (all uuid) |
| Policy | CreatePolicyRequest | name, condition, expression (priority optional int32, default 100) |
| Quota Policy | DefineQuotaPolicyRequest | scopeKind (PrincipalKind), scopePrincipalId (uuid), daily, weekly, monthly (int64) |
| Subscription | AssignSubscriptionRequest | policyId (uuid), scopeKind (PrincipalKind), scopePrincipalId (uuid) |
| Consumption | RecordConsumptionRequest | consumerId (uuid), actionKey, units (int64), idempotencyKey |
| Principal edge | UpsertPrincipalEdgeRequest | operation (enum); nodeId/userId/roleId optional uuids |
| Authorization decision | EvaluateAuthorizationRequest | subjectId, action, resourceType, resourceId, ownerId, resourceAttributes, environmentAttributes, usageAttributes |

Enums to resolve to valid integer values before use: `PlanTier`, `TenantStatus`, `PrincipalKind`,
`QuotaWindow`, `PrincipalHierarchyOperation`, `ConsumptionDecisionType` (all `type: integer`, no
enum member list published — valid values must be confirmed during execution, not assumed).

---

## 10. Cross-Service Dependencies (documented, not executed)

| Integration | Flow |
|---|---|
| Identity → Tenant | Login/registration establishes user; tenant context (X-Tenant-Id) required by Tenant/Policy calls |
| Identity → Authorization | AuthorizationService validates the JWT issued by Identity (JWT warmup confirmed in Authz startup log) |
| Tenant → Authorization | Department created in Tenant is the `departmentId` required by CreateRole in Authz |
| Authorization ← Policy | Consumption/quota attributes (usageAttributes, usageLimit) feed authorization decisions |
| Policy → Authorization | RecordConsumption result and quota resolution inform Decisions/record-result |
| Messaging (RabbitMQ) | Authz publishes to authorization.cache, audit.pipeline, opa.sync, analytics.pipeline (seen in bus config); OPA sync integration present |

These are marked as integrations only. They will be validated in the Messaging / Database
Verification phases, not now.

---

## 11. Idempotent Operations (mark for later validation)

| Operation | Reason |
|---|---|
| PUT /api/v1/tenants/settings | PUT semantics — repeat yields same state |
| PATCH /tenants/{id}/status, /plan | converge to target state |
| PATCH /quotas/{id}, /policies/{id}/condition | converge to target values |
| POST /consumption/record | carries explicit `idempotencyKey` — must dedupe |
| POST /administration/allowance/reset | reset is naturally idempotent |
| POST /subscriptions/{id}/activate,revoke,supersede | terminal-state transitions, repeat should be no-op/consistent |
| POST /principal-hierarchy/edges | named "Upsert" — insert-or-update |
| DELETE …/departments/{id}, /quotas/{id} | delete then re-delete → 404 (idempotent-safe) |

---

## 12. Validation Rules (from schemas — for Negative Testing phase)

- Required fields: enumerated per request schema in Section 9.
- Formats: `uuid` on all id fields; `date-time` on SubscriptionDto.effectiveFrom; numeric fields use
  `int32`/`int64` with pattern `^-?(?:0|[1-9]\d*)$` (serialized as string-or-integer).
- Nullable fields: description (department/permission/role), parentRoleId, LoginRequest identifier/
  userName/mfaCode, RecordConsumptionResponse.reason, PolicyDto.compiledRegoHash.
- Enums (integer-typed, member lists not published): PlanTier (default 1), TenantStatus, PrincipalKind,
  QuotaWindow, PrincipalHierarchyOperation, ConsumptionDecisionType — negative tests should probe
  out-of-range integers.
- Defaults: CreatePolicyRequest.priority = 100; CreateTenantRequest.planTier = 1.
- No explicit min/max/string-length/regex patterns are declared in the documents beyond the numeric
  serialization pattern above. String-length and business-rule limits are enforced in code and are not
  in the document; negative testing must probe these empirically rather than assume bounds.

---

## 13. Execution Plan (immutable ordering for Phases 4+)

```
1.  Environment            — start all 4 services in Development, confirm /health/ready = 200
2.  Authentication         — register → login → obtain JWT + refresh; mfa enable/verify
3.  Bootstrap Data         — CreateTenant → CreateDepartment; CreatePermission → CreateRole → GrantPermission → AssignRole
4.  CRUD                   — Tenant, Department, Policy, Quota, Subscription create→read→update→list→delete chains
5.  Actions                — publish/archive policy, activate/revoke/supersede subscription, tenant status/plan, user disable/activate/unlock
6.  Read APIs              — list/get across all services, slug lookup, effective-permissions, ledgers usage/debt, allowance status
7.  Negative Tests         — required-field omission, bad uuids, out-of-range enums, conflict (409) and not-found (404) paths
8.  Messaging              — RabbitMQ (authorization.cache, audit.pipeline, opa.sync, analytics.pipeline), OPA sync
9.  Database Verification  — persisted state for tenants, departments, policies, subscriptions, ledgers
10. Regression             — full re-run of the ordered sequence
```

This ordering is fixed and must not be reordered in later phases unless a bug requires it.

---

## Recommendation

All Phase-3 exit criteria are met:

- ✔ OpenAPI successfully loaded (4/4 services, HTTP 200, OpenAPI 3.1.1)
- ✔ Scalar available (4/4 services, HTTP 200)
- ✔ Every endpoint discovered (68 operations, from the documents only)
- ✔ Every operation categorized
- ✔ Authentication requirements identified (with documented over-declaration caveat)
- ✔ Dependency graph generated
- ✔ Test execution order generated
- ✔ Required test data identified

Open caveats carried forward (do not block, but must be handled in later phases):
1. IdentityService must be started in the Development environment (SigningKey lives in
   `appsettings.Development.json`); OpenAPI/Scalar are Development-only.
2. The OpenAPI security block is applied uniformly to all operations, so anonymous vs protected and
   fine-grained policy names (PlatformServiceOnly/PlatformAdmin) are not derivable from the document
   and must be observed empirically during execution.
3. Integer enums publish no member lists; valid values must be confirmed at execution time.

### PASS

Phase 4 may proceed.
