# ADR-013

Title:

Tenant Resolution, Selection, and Propagation — TenantId Ownership and Lifecycle

Status:

Accepted

---

# 1. Context

The platform is multi-tenant. Tenant isolation is enforced by a Postgres Row-Level-Security
interceptor (`TenantRlsInterceptor`) that reads `TenantId` from the request execution context
(`IRequestContextAccessor.Context.TenantId`) and issues
`SET LOCAL app.current_tenant_id`.

The previous ADR-013 draft focused on removing the duplicate tenant-provider/resolver code and
simplifying `TenantId` access, but it did **not** clearly separate three distinct
responsibilities that must not be mixed:

1. **Tenant Resolution** — discovering `TenantId` *before* authentication / before JWT issuance.
2. **Tenant Selection** — choosing the active tenant when a principal belongs to several.
3. **Tenant Propagation** — flowing an already-known `TenantId` through an authenticated request.

This ADR re-defines ownership, lifecycle, and boundaries, and adds explicit mandatory rules so
that the upcoming `RequestContext` consolidation (ADR-012) is built on a correct foundation.

---

# 2. Problem Statement

- The codebase contains a live propagation path (Platform-owned) **and** a registered-but-inert
  service-API tenant-provider family (`ITenantResolver` / `*TenantProvider`) that would resolve
  `TenantId` independently of Platform. This mixes Resolution with Propagation and creates a
  header-vs-JWT ambiguity landmine.
- There is no explicit statement that `TenantId`, once carried by a signed JWT, must **never** be
  re-resolved at runtime.
- There is no explicit statement that a missing `TenantId` after successful authentication is a
  security failure, not a recoverable condition.
- Cache ownership is ambiguous: the slug→id cache must belong to Identity business flows, never
  to Platform.

These gaps must be closed before consolidating `RequestContext`.

---

# 3. TenantId Lifecycle Model

The lifecycle has exactly two phases, separated by JWT issuance:

```
┌─────────────────────────── PRE-AUTHENTICATION (Resolution) ───────────────────────────┐
│ IdentityService authentication flows (login, invitation, domain, email discovery)      │
│   discovers TenantId                                                                   │
│   - may call TenantService (slug/domain → id)                                          │
│   - may use cache (slug → id)                                                          │
│   - is business logic                                                                  │
│   - result is embedded into the issued JWT as the `tenant_id` claim                    │
└───────────────────────────────────────────┬───────────────────────────────────────────┘
                                             │  JWT issued (tenant_id claim present)
                                             ▼
┌─────────────────────────── POST-AUTHENTICATION (Propagation) ────────────────────────┐
│ JWT                                                                                    │
│   │                                                                                    │
│   ▼                                                                                    │
│ PrincipalResolutionMiddleware        [Platform]  invokes CurrentPrincipalFactory       │
│   │   CurrentPrincipalFactory parses the *already-issued* tenant_id claim              │
│   │     (claim parsing = propagation, NOT resolution)                                  │
│   ▼                                                                                    │
│ ICurrentPrincipal.TenantId                                                             │
│   │                                                                                    │
│   ▼                                                                                    │
│ TenantMiddleware                      [Platform]  Context.TenantId = principal.TenantId│
│   │   HttpContext.Items["TenantId"] = principal.TenantId                              │
│   ▼                                                                                    │
│ IRequestContextAccessor.Context       [Platform — read-only exposure]                 │
│   ├─ TenantBehavior        (reject TenantId == Empty unless bypass)                    │
│   ├─ AuthorizationBehavior (authz decision keyed by TenantId)                          │
│   ├─ TenantRlsInterceptor  (SET LOCAL app.current_tenant_id)                           │
│   └─ Application handlers  (read Context.TenantId)                                     │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

**Tenant Selection** (multiple tenants per principal) is **out of scope** for this ADR. The
current model assigns exactly one Tenant (and one Department, one Role) per User. No selection
abstraction is designed or required here.

---

# 4. Responsibility Boundaries

| Concern | Owner | Phase | May call TenantService? | May use cache? | Is business logic? |
|---|---|---|---|---|---|
| Tenant Resolution | **IdentityService** (auth flows) | Pre-auth | Yes | Yes | **Yes** |
| Tenant Selection | (none — out of scope) | n/a | n/a | n/a | n/a |
| Tenant Propagation | **Platform** | Post-auth | **No** | **No** | No (infra) |

- **IdentityService owns Tenant Resolution.** It runs only inside authentication flows
  (e.g. `LoginCommandHandler` resolving a slug → `TenantServiceClient.ResolveTenantIdBySlugAsync`,
  then `HmacJwtTokenGenerator` embedding `tenant_id` into the JWT).
- **Platform owns Tenant Propagation only.** `PrincipalResolutionMiddleware`,
  `CurrentPrincipalFactory`, `TenantMiddleware`, and `IRequestContextAccessor` read an
  already-issued value; they never look it up.
- **AuthorizationService never resolves TenantId.** It consumes `Context.TenantId` for
  authorization decisions only.
- **TenantService never resolves TenantId for authenticated requests.** It only answers
  discovery queries (slug/domain → id) invoked by IdentityService during Resolution.

---

# 5. Allowed Resolution Points

`TenantId` may be resolved **only** at these points, all pre-authentication and all inside
IdentityService:

1. Login with tenant slug — `LoginCommandHandler` →
   `ITenantServiceClient.ResolveTenantIdBySlugAsync` (`LoginCommandHandler.cs:50`), cached by
   `CachedTenantServiceClient` (`CachedTenantServiceClient.cs:18`, key
   `platform:shared:tenant:slug:{slug}`, TTL 5 min).
2. Login with domain / invitation token / email-based discovery — *if and when supported*,
   likewise owned by IdentityService authentication flows and may use `TenantService` + cache.
3. JWT issuance — `HmacJwtTokenGenerator.GenerateAccessToken` writes
   `tenant_id = user.TenantId` into the token (`HmacJwtTokenGenerator.cs:60`). This "bakes in"
   the resolved value; it is the boundary between Resolution and Propagation.

No other component may resolve `TenantId`.

---

# 6. Forbidden Resolution Points

After a JWT containing `tenant_id` is issued, **no runtime component may resolve `TenantId`
again**. Explicitly forbidden:

- `PrincipalResolutionMiddleware` — must only parse the issued claim, never look up.
- `CurrentPrincipalFactory` — must only parse the issued claim, never look up.
- `TenantMiddleware` — must only expose `principal.TenantId`, never look up.
- `IRequestContextAccessor` / `HttpRequestContextAccessor` — must only read
  `HttpContext.Items`, never look up (`HttpRequestContextAccessor.cs:19` already reads
  `Items["TenantId"]`; this must remain lookup-free).
- `TenantRlsInterceptor` / `DbContext` — must only read `Context.TenantId`, never resolve.
- MediatR behaviors (`TenantBehavior`, `AuthorizationBehavior`) — must only read
  `Context.TenantId`.
- Application handlers / application services — must only read `Context.TenantId`.
- **Any `ITenantResolver` / `*TenantProvider` (Jwt/Header/Session/Host) in service APIs** —
  must never be wired into the propagation pipeline. These are currently registered
  (`ServiceCollectionExtensions.cs:133-137`) but have **no caller**; they are dead code and a
  latent violation of the propagation-only rule.

---

# 7. Request Pipeline Flow (Propagation)

```
JWT (signed, carries tenant_id claim)
  → PrincipalResolutionMiddleware      [Platform]  calls CurrentPrincipalFactory
  → CurrentPrincipalFactory            [Platform]  parses tenant_id claim → ICurrentPrincipal.TenantId
  → ICurrentPrincipalAccessor.Principal
  → TenantMiddleware                   [Platform]  Context.TenantId = principal.TenantId
  → IRequestContextAccessor.Context    [Platform — read-only]
       ├─ TenantBehavior        (reject TenantId == Empty unless CanBypassTenantIsolation)
       ├─ AuthorizationBehavior (authz keyed by TenantId)
       ├─ TenantRlsInterceptor  (SET LOCAL app.current_tenant_id)
       └─ Application handlers   (read Context.TenantId)
```

No lookup · no TenantService call · no cache access · no fallback · no provider chain.

---

# 8. Cache Ownership Rules

- The slug→id cache (`platform:shared:tenant:slug:{slug}`) is used **during tenant discovery**
  and therefore belongs to **Identity/Application business flows** (`CachedTenantServiceClient`
  in `IdentityService.Infrastructure`). It is invalidated on `TenantCreatedV1` by
  `TenantCreatedCacheInvalidationConsumer` (IdentityService) and `TenantCacheInvalidationConsumer`
  (TenantService).
- **Platform must never contain `slug → cache → tenant` lookup.** Platform's responsibility
  ends at:
  ```
  JWT  →  CurrentPrincipal  →  RequestContext
  ```
- `CurrentPrincipalFactory`, `TenantMiddleware`, and `IRequestContextAccessor` perform **no
  cache access** of any kind.
- Cache is an optimization for Resolution, never a source of truth, and never part of
  Propagation.

---

# 9. Security Implications

- `TenantId` in the propagation phase is derived **only** from the cryptographically signed JWT
  (`tenant_id` claim). A client cannot spoof it without the HMAC signing key. RLS trusts it.
- **Header `X-Tenant-Id` (inbound) is not a resolver.** The only inbound reader
  (`HeaderTenantProvider`) is dead code; the live `X-Tenant-Id` header is outbound (TenantService
  clients send it to TenantService). If the dead provider were ever wired, `TenantResolver`
  returns `resolutions[0]` (first non-null) and a header could override a signed JWT — a
  security landmine. The propagation-only rule forbids this.
- A stale slug→id cache cannot cause cross-tenant access: slug→tenant mapping is immutable, and
  invalidation on `TenantCreatedV1` exists for availability, not isolation.
- **Missing `TenantId` after authentication is a security failure, not a recoverable
  condition.** `TenantBehavior` already rejects `TenantId == Guid.Empty`
  (`TenantBehavior.cs:27-37`) unless `CanBypassTenantIsolation` (service principals). There is
  **no runtime recovery / rediscovery** of `TenantId` after auth, and there must be none.

---

# 10. Mandatory Architectural Rules

1. `TenantId` may be resolved **only before authentication** (during Resolution).
2. After a JWT containing `tenant_id` is issued, **no runtime component may resolve `TenantId`
   again**.
3. **IdentityService owns Tenant Resolution.**
4. **Platform owns Tenant Propagation only.**
5. **AuthorizationService never resolves `TenantId`.**
6. **TenantService never resolves `TenantId` for authenticated requests** (only answers
   pre-auth discovery queries for IdentityService).
7. `RequestContext` is **read-only**.
8. `RequestContextAccessor` **never performs lookups**.
9. `TenantMiddleware` **never performs lookups**.
10. `DbContext` **never performs Tenant resolution**.
11. Handlers and application services **never perform Tenant resolution**.
12. **Missing `TenantId` after authentication is a security failure, not a recoverable
    condition.**

13. **`IResolveTenantInternally` is the only allowed exception to missing-`TenantId`
    rejection.** Requests implementing this marker represent **pre-authentication Tenant
    Resolution flows only**. They must **never** be used for authenticated-request tenant
    recovery. (See `Platform.Behaviors.TenantBehavior`, which skips enforcement for
    `IResolveTenantInternally` requests — these are Resolution-phase operations, not a
    runtime fallback.)

14. **`TenantId` must never be accepted from inbound request headers.** Headers such as
    `X-Tenant-Id` cannot establish tenant identity. The only trusted runtime source is the
    authenticated JWT `tenant_id` claim. (Inbound `X-Tenant-Id` readers are dead code; the
    live header is outbound-only, sent by TenantService clients.)

If authentication succeeds but `TenantId` is missing:
- Do **NOT** try to discover `TenantId`.
- **Reject** the request.
- Treat it as an **invalid authentication state** (logged, 401/403).

---

# 11. Consequences

Positive:
- Clear separation of Resolution (Identity, pre-auth, business) vs Propagation (Platform,
  post-auth, infrastructure).
- Single authoritative per-request `TenantId` source: the signed JWT.
- Removes header-vs-JWT ambiguity and the SRP/security landmine posed by the dead provider
  family.
- Makes the upcoming `RequestContext` consolidation (ADR-012) safe: the unified accessor is
  propagation-only.

Negative / notes:
- The dead `ITenantResolver` / `*TenantProvider` family remains in the codebase (this ADR does
  **not** remove classes). It is inert today but must never be invoked in the propagation
  pipeline; its eventual deletion is deferred cleanup, not required for ADR-012.
- Tenant Selection is explicitly out of scope; no abstraction is introduced.

---

# 12. Migration Impact for the Future ADR-012 Refactor

- ADR-012 (consolidate `RequestContext` / `IRequestContextAccessor` into Platform) is
  **unaffected** by this ADR's ownership model: the unified `Platform…IRequestContextAccessor`
  is purely a propagation/exposure object and already satisfies rules 7–9, 11.
- ADR-012 Phase 0 (remove `RequestContext` from Authorization command contracts) remains valid
  and independent.
- During the ADR-012 accessor consolidation, the dead service-API providers must **not** be
  wired into the new Platform pipeline. The unified accessor must remain lookup-free
  (rules 8–9), reading only `HttpContext.Items` / the parsed principal.
- The slug→id cache and `TenantService` login-time lookup stay in IdentityService and are out of
  scope for the `RequestContext` refactor.

---

# 13. Open Questions

1. `CachedTenantServiceClient` defines `NotFoundSentinel` but readers use
   `cache.GetAsync<SharedTenantCacheDto>`, which will not deserialize the sentinel string. Is
   negative caching intended? If so, the read path must honor it.
2. `LoginCommandHandler` username-only path uses `context.TenantId`
   (`LoginCommandHandler.cs:68`); for an unauthenticated login this is `Guid.Empty` and fails
   with `TenantMissing`. Is username-only login expected, and if so what supplies the tenant
   (the `slug/username` identifier path is the supported Resolution route)?
3. `TenantCacheInvalidationConsumer.HandleTenantChanged` also invalidates
   `tenant-service:tenant:id:{tenantId}` — confirm this is the TenantService-internal tenant
   cache and unrelated to per-request propagation.
4. If a token-less scenario (e.g. session-only) is ever needed, the session→tenant fallback
   must be implemented as a **Platform** resolver invoked during Resolution, never in a service
   API — currently deferred.
