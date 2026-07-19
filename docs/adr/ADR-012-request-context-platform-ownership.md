# ADR-012

Title:

Platform Ownership of the Request Context Abstraction

Status:

Accepted

---

# Context

The current request execution context (`RequestContext` and `IRequestContextAccessor`)
is duplicated across the codebase. A discovery review found four accessor interfaces
and four `RequestContext` record types:

- `Platform.Abstractions.Tenant.IRequestContextAccessor` (A) + `Platform.Abstractions.Tenant.RequestContext` (R1) — the shared-kernel contract consumed by cross-cutting infrastructure.
- `IdentityService.Application.Common.Abstractions.IRequestContextAccessor` (B, subtype of A) + `RequestContext` (R2).
- `TenantService.Application.Common.Abstractions.IRequestContextAccessor` (C, **not** a subtype of A) + `RequestContext` (R3, identical to R2).
- `AuthorizationService.Application.Common.Abstractions.IRequestContext` (D, subtype of A) + `RequestContext` (R4).

Every service also carries its own `HttpRequestContextAccessor` implementation and a
duplicated dual DI registration. The repeated DI failures (`IPlatformServiceRegistry`,
`IRequestContextAccessor`) are symptoms of this duplication, not isolated mistakes.

Two additional duplications were found during verification:

- `TenantSource` enum is defined twice, identically, in `IdentityService.Application.Common.Abstractions.TenantSource.cs` and `TenantService.Application.Common.Abstractions.TenantSource.cs`. **Platform defines none.**
- `Platform.Abstractions.Tenant.ITenantContext` is orphaned: declared, never registered, never consumed (verified — only the definition line exists).

---

# 1. Why RequestContext is a Platform concern

Per the established platform boundaries, Platform owns the *current request context*,
*tenant context*, *correlation id*, *user id*, and *request metadata*. The code confirms this:

- `Platform.Infrastructure.Persistence.TenantRlsInterceptor` (the component that enforces
  tenant isolation at the database layer) depends on `Platform…IRequestContextAccessor`
  to read `TenantId` and inject `SET LOCAL app.current_tenant_id`.
- `Platform.Middleware.TenantMiddleware`, `Platform.Behaviors.TenantBehavior`, and
  `Platform.Behaviors.AuthorizationBehavior` all consume the Platform accessor.

These are infrastructure-level, cross-cutting concerns. Individual services are
**consumers only**; none of them owns or defines tenant isolation, correlation, or
principal resolution. Therefore the canonical request-context model belongs in
`Platform.Abstractions` (the shared kernel), not in any service's Application layer.

---

# 2. Why the duplication is an architectural bug, not intentional design

- R2 and R3 are byte-for-byte identical records.
- R1 already contains the security-relevant fields (`PrincipalType`, `UserName`, `Roles`,
  `ServiceName`); R2/R3/R4 are projections that *dropped* those and instead added
  telemetry fields (`IpAddress`, `UserAgent`, `TenantSource`). Verification shows
  `IpAddress`, `UserAgent`, and `TenantSource` are **never read** anywhere in service code —
  they exist only as declared shape, not as used data.
- B and D correctly subtype A; C silently does **not**, which means TenantService's
  accessor cannot be consumed generically by Platform behaviors/middleware — a latent
  inconsistency, not a deliberate boundary.
- Each service re-implements `HttpRequestContextAccessor` reading the *same*
  `HttpContext.Items` keys (`TenantId`, `CorrelationId`, `RequestId`, `CurrentPrincipal`).
- The pattern is contradicted by `ICurrentPrincipal`, which is already centralized in
  `Platform.Abstractions.Principal` and reused everywhere with no service-level copies.
  The context family should follow that same proven pattern.

The duplication was caused by per-service copy-paste of a cross-cutting abstraction,
then bridged with dual DI registrations — the direct cause of the runtime DI failures.

---

# 3. Target Architecture

A single, Platform-owned abstraction:

```csharp
// Platform.Abstractions.Tenant.RequestContext  (single record, R1 extended)
public sealed record RequestContext(
    Guid TenantId,
    Guid CorrelationId,
    Guid RequestId,
    Guid? UserId = null,
    string? UserName = null,
    string[]? Roles = null,
    Guid? DepartmentId = null,
    PrincipalType PrincipalType = PrincipalType.Unknown,
    string? ServiceName = null,
    bool CanBypassTenantIsolation = false,
    // NOTE: expansion with IpAddress / UserAgent / TenantSource is DEFERRED
    // until after Phase 0 (command-contract decoupling) and Phase 1 below.
    string? IpAddress = null,
    string? UserAgent = null,
    TenantSource TenantSource = TenantSource.Unknown);

// Platform.Abstractions.Tenant.IRequestContextAccessor  (single interface)
public interface IRequestContextAccessor
{
    RequestContext Context { get; set; }
}
```

- **Owner:** `Platform.Abstractions` (shared kernel / cross-cutting contract).
- **Namespace:** `Platform.Abstractions.Tenant`.
- **Services:** depend directly on `Platform.Abstractions.Tenant.IRequestContextAccessor`.
  No service-level `IRequestContextAccessor` / `IRequestContext` / `RequestContext` types.
- **Implementation:** one `HttpRequestContextAccessor` inside the Platform ASP.NET Core
  infrastructure package, reading `HttpContext.Items` and projecting into the unified record.
- **Registration:** one Platform extension method (e.g. `AddPlatformRequestContext`)
  replacing the three per-service dual registrations.

Clean Architecture is preserved: `Service.*.Application` → `Platform.Abstractions`
(inward, stable-abstraction dependency). Verified `Platform.*` projects reference only
`Platform.Abstractions`, `Platform.Caching`, and `SharedKernel` — Platform never references
any `Service.*.Application`.

---

# Architectural Rules

These rules govern the execution context and are binding for this refactoring and all
future development:

1. **Single authoritative source.** There must be exactly one authoritative source of
   execution context during a request.
2. **The source is the Platform accessor.** That source is
   `Platform.Abstractions.Tenant.IRequestContextAccessor`. No other type or instance may
   be treated as the execution-context authority.
3. **Execution context is infrastructure state, not business data.** It describes *how*
   and *by whom* a request is executed; it is not part of any domain model.
4. **Execution context must never be persisted.** It must not be written to a database,
   cache, or any durable store as if it were business state.
5. **Execution context must never be embedded into Commands.** Application command/query
   records express business intent; they must not carry `RequestContext`.
6. **Execution context must never be embedded into Domain Events.** Event payloads are
    business facts and may cross process/time boundaries; they must not embed execution context.
7. **Execution context must never become part of Aggregate state.** Aggregates persist
   business state only; execution context is ambient and ephemeral.

Violations of rules 5–7 found during discovery (Authorization command contracts embedding
`RequestContext`) are addressed in Phase 0 below.

---

# 4. Staged Migration Plan

The migration is incremental and behavior-preserving. No behavioral changes in any phase.

## Phase 0 — Remove execution context from application contracts (new, pre-consolidation)

This phase resolves a more fundamental issue than the duplication itself: execution
context was embedded into Authorization command/query contracts. It is performed **before**
any `RequestContext` model expansion.

- Remove `RequestContext` from **all** Authorization command/query contracts:
  `RevokeRoleCommand`, `RevokePermissionCommand`, `GrantPermissionCommand`,
  `CreateRoleCommand`, `CreatePermissionCommand`, `AssignRoleCommand`,
  `ChangeUserRoleCommand`, `GetEffectivePermissionsQuery`, `InvalidateAuthorizationCacheCommand`,
  `EvaluateAuthorizationDecisionCommand`, `EvaluateBatchAuthorizationDecisionsCommand`,
  `RecordOperationResultCommand`.
- Inject `Platform.Abstractions.Tenant.IRequestContextAccessor` into the handlers and read
  `TenantId` / `CorrelationId` / `UserId` / `DepartmentId` from the accessor.
- Replace **every** `command.Context` / `request.Context` usage with the accessor.
- Delete `IAuthorizationDecisionRequest` **if it has no remaining consumers** after the
  migration (verified during discovery: it was implemented only by
  `EvaluateAuthorizationDecisionCommand` and consumed nowhere).
- Remove `EndpointContext.From` (Authz) **only if it becomes completely unused** after the
  migration; otherwise leave it for any non-context usages.

## Phase 1 — Introduce `Platform.TenantSource`; convert to named arguments (no expansion)

- Introduce `Platform.Abstractions.Tenant.TenantSource` (move the enum out of
  `IdentityService.Application.Common.Abstractions.TenantSource` and
  `TenantService.Application.Common.Abstractions.TenantSource`; repoint `TenantResolution`,
  tenant providers, and the accessors).
- Replace **all positional `RequestContext` construction** with **named arguments** (or a
  `RequestContextFactory` helper) in the three service `HttpRequestContext(Accessor)`
  implementations and the Identity integration test. This protects against the later
  constructor-signature change silently mis-mapping fields.
- **Do not expand `Platform.RequestContext` yet.** Field expansion is deferred until Phase 0
  is complete and the command-contract question is resolved.

## Phase 2 — Reduce service interfaces to compatibility aliases

- Keep `IdentityService…IRequestContextAccessor` (B) and `AuthorizationService…IRequestContext`
  (D) temporarily, but strip all declared members so each is **only** a marker inheriting from
  `Platform.Abstractions.Tenant.IRequestContextAccessor`.
  - B: `public interface IRequestContextAccessor : Platform.Abstractions.Tenant.IRequestContextAccessor { }`
  - D: `public interface IRequestContext : Platform.Abstractions.Tenant.IRequestContextAccessor { }`
- Fix C (`TenantService…IRequestContextAccessor`) to also inherit from the Platform interface
  (currently it does not), making it a pure alias as well.
- The `RequestContext` return type on these aliases resolves to the unified Platform record.

## Phase 3 — Migrate consumers to the Platform abstraction

- Update every Application handler, Domain service, and `DbContext` to depend directly on
  `Platform.Abstractions.Tenant.IRequestContextAccessor` (drop the `using …Common.Abstractions`
  for the context type).
- Affected: 13 Identity handlers (`RegisterCommandHandler`, `LoginCommandHandler`, …),
  `IdentityDbContext`; Tenant handlers (`CreateTenant`, `UpdateTenantPlan`, `CreateDepartment`, …)
  and `TenantDbContext`; Authorization handlers.
- Mechanical rename only; property usage (`TenantId`, `CorrelationId`, `DepartmentId`,
  `CanBypassTenantIsolation`) is identical on the unified record.

## Phase 4 — Remove compatibility interfaces and duplicate records

- Once no consumer references B/C/D or R2/R3/R4, delete:
  - `IdentityService.Application.Common.Abstractions.IRequestContextAccessor` + `RequestContext`
  - `TenantService.Application.Common.Abstractions.IRequestContextAccessor` + `RequestContext`
  - `AuthorizationService.Application.Common.Abstractions.IRequestContext` + `RequestContext`
- The single surviving types are `Platform.Abstractions.Tenant.IRequestContextAccessor` + `RequestContext`.

## Phase 5 — Consolidate implementation and registration

- Remove the three duplicated `HttpRequestContextAccessor` classes from
  `IdentityService.Api`, `TenantService.Api`, `AuthorizationService.Api`.
- Introduce one `HttpRequestContextAccessor : Platform.Abstractions.Tenant.IRequestContextAccessor`
  inside the Platform ASP.NET Core infrastructure package.
- Replace the three per-service dual registrations with a single Platform extension
  (e.g. `services.AddPlatformRequestContext()`), called from each service's
  `ServiceCollectionExtensions`.

---

# 5. Obsolete Type Verification — `ITenantContext`

`Platform.Abstractions.Tenant.ITenantContext` is **obsolete and safe to remove**:

- It declares only `Guid TenantId { get; }`.
- A repo-wide search (all `.cs`) finds exactly one reference: its own definition.
- It is never registered (no `AddSingleton/AddScoped<ITenantContext>`) and never injected.

Recommendation: delete `ITenantContext` during Phase 4 (or Phase 1) with no migration risk.
Its intent (expose current tenant id) is fully covered by the unified `RequestContext.TenantId`.

---

# Consequences

Positive:

- Single source of truth for the request context — eliminates the duplicated models and the DI-failure class.
- Clean Architecture preserved: services depend inward on the Platform shared kernel.
- Removes per-service `HttpRequestContextAccessor` and dual-registration boilerplate.
- Aligns the context family with the already-centralized `ICurrentPrincipal` pattern.

Negative:

- Cross-cutting change touching three service Application/DbContext/API projects (mitigated by the staged, behavior-preserving plan).
- Temporary compatibility aliases exist for one phase (managed in Phase 4).

The binding constraints for this refactoring and future development are stated in the
**Architectural Rules** section above (single authoritative execution-context source,
infrastructure state only, never persisted, never embedded in Commands, Domain Events, or
Aggregate state). `HttpRequestContextAccessor` must remain request-scoped (or stateless as a
pure `HttpContext` projection) and must never cache `RequestContext` across requests.

---

# Detailed Implementation Plan (Phase 0 Preparation — ADR-013 aligned)

This plan is prepared for implementation. It follows ADR-013: `RequestContext` is read-only,
exposes the authenticated tenant identity, and **never** performs lookup, cache access,
`TenantService` calls, or missing-`TenantId` resolution. The request flow remains:

```
JWT → PrincipalResolutionMiddleware → CurrentPrincipalFactory → RequestContext → Application
```

## 1. Current State

**Interfaces**
- `A` `Platform.Abstractions.Tenant.IRequestContextAccessor` — canonical (consumed by
  `TenantRlsInterceptor`, `TenantMiddleware`, `TenantBehavior`, `AuthorizationBehavior`).
- `B` `IdentityService.Application.Common.Abstractions.IRequestContextAccessor` — `: A`.
- `C` `TenantService.Application.Common.Abstractions.IRequestContextAccessor` — **not** `: A`
  (divergent).
- `D` `AuthorizationService.Application.Common.Abstractions.IRequestContext` — `: A`.
- `IAuthorizationDecisionRequest` (Authz) — exposes `RequestContext Context`; only implemented
  by `EvaluateAuthorizationDecisionCommand`, no consumer.

**Records**
- `R1` `Platform.Abstractions.Tenant.RequestContext` (canonical).
- `R2` `IdentityService…RequestContext`, `R3` `TenantService…RequestContext` (≡ R2),
  `R4` `AuthorizationService…RequestContext`.
- `TenantSource` enum duplicated in `IdentityService` and `TenantService` (Platform defines none).

**Implementations**
- `Platform.Infrastructure.Tenant.RequestContextAccessor` (scoped default).
- `IdentityService.Api.Infrastructure.HttpRequestContextAccessor` (impl `B + A`).
- `TenantService.Api.Infrastructure.HttpRequestContextAccessor` (impl `C + A`).
- `AuthorizationService.Api.Infrastructure.HttpRequestContext` (impl `D + A`).

**Consumers**
- Middleware: `TenantMiddleware`. Behaviors: `TenantBehavior`, `AuthorizationBehavior`.
  Interceptor: `TenantRlsInterceptor`.
- Application: 12 Identity handlers, 6 Tenant handlers, Authz handlers + 12 command/query
  records embedding `R4 Context`.
- Infrastructure/Persistence: `IdentityDbContext`, `TenantDbContext`, `AuthorizationDbContext`
  (expression trees on `TenantId/DepartmentId/CanBypassTenantIsolation`) + 3 factory null
  stubs.
- API: 3 `EndpointContext.From` (positional `RequestContext` builds).
- Tests: `IdentityService.IntegrationTests.ConcurrencyInvestigationTests` (`Mock<IRequestContextAccessor>`,
  positional `R2`).

**DI registrations**: Platform `AddScoped<IRequestContextAccessor,RequestContextAccessor>`;
3 per-service dual registrations.

## 2. Target State

- One interface `Platform.Abstractions.Tenant.IRequestContextAccessor` (canonical `A`).
- One record `Platform.Abstractions.Tenant.RequestContext` (extended with `IpAddress`,
  `UserAgent`, `TenantSource` — deferred until after Phase 0/1; field *names* preserved so
  DbContext expression trees keep working).
- One implementation `Platform…HttpRequestContextAccessor` (read-only projection of
  `HttpContext.Items` / parsed principal; **no lookup/cache/TenantService**), registered by a
  single `AddPlatformRequestContext()` extension.
- All handlers/DbContexts depend directly on `A`. No service-level `IRequestContextAccessor`,
  `IRequestContext`, `RequestContext`, or `TenantSource`.
- Authz command/query contracts no longer embed `RequestContext` (Phase 0).

## 3. Interfaces / Classes Affected

- **Delete (Phase 4):** `B`, `C`, `D`, `IAuthorizationDecisionRequest`, `R2`, `R3`, `R4`,
  duplicate `TenantSource` enums, the 3 service `HttpRequestContext(Accessor)` impls,
  `ITenantContext` (orphan).
- **Add (Phase 1):** `Platform.Abstractions.Tenant.TenantSource`; named-arg `RequestContext`
  construction / optional `RequestContextFactory`.
- **Modify:** `IdentityDbContext`, `TenantDbContext`, `AuthorizationDbContext`;
  12 Identity + 6 Tenant + Authz handlers; 12 Authz command records; 3 `EndpointContext.From`;
  3 per-service `ServiceCollectionExtensions`; `IdentityService.IntegrationTests`.
- **Protected (unchanged):** authentication flow, JWT generation (`HmacJwtTokenGenerator`),
  `PrincipalResolutionMiddleware`, `CurrentPrincipalFactory`, `TenantMiddleware`, dead
  `ITenantResolver`/`*TenantProvider` family, `TenantService` discovery, `CachedTenantServiceClient`.

## 4. Migration Order

1. **Phase 0 — Decouple command contracts (pre-consolidation).** Remove `RequestContext` from
   the 12 Authz command/query records; inject `Platform…IRequestContextAccessor` into handlers;
   replace `command.Context` usages; delete `IAuthorizationDecisionRequest` if no consumers;
   remove `EndpointContext.From` (Authz) only if fully unused.
2. **Phase 1 — Minimal, deferred expansion.** Introduce `Platform.TenantSource`; convert **all**
   positional `RequestContext` construction to named arguments (or a factory) in the 3 service
   impls + integration test. **Do not expand `Platform.RequestContext` yet.**
3. **Phase 2 — Alias reduction.** Make `B`, `D` pure markers `: A`; fix `C` to also be `: A`.
4. **Phase 3 — Consumer migration.** Point all handlers/DbContexts at `A`.
5. **Phase 4 — Delete duplicates** (`B/C/D`, `R2/R3/R4`, `IAuthorizationDecisionRequest`,
   duplicate `TenantSource`, `ITenantContext`).
6. **Phase 5 — Single impl + extension.** One `Platform…HttpRequestContextAccessor` via
   `AddPlatformRequestContext()`; remove 3 per-service impls/registrations.

## 5. Compatibility Risks

- **Positional construction** in `EndpointContext.From` (×3), the 3 impls, and the integration
  test will silently mis-map once `Platform.RequestContext` field order changes → mitigated by
  Phase 1 named-arg conversion *before* any expansion.
- **`C` not `: A`** → fixed in Phase 2 so TenantService consumers bind to the canonical accessor.
- **DbContext expression trees** use `nameof(RequestContext.TenantId/DepartmentId/CanBypassTenantIsolation)`
  → preserve these field names on the unified record.
- **Authz command records embedding `R4`** → removed in Phase 0, shrinking blast radius.
- **Integration test** `Mock<IRequestContextAccessor>` + positional `R2` → updated in Phase 1.

## 6. Tests Required

- Update `ConcurrencyInvestigationTests` to named-arg `Platform…RequestContext` (Phase 1).
- Add/extend a unit test for the single `Platform…HttpRequestContextAccessor` projecting
  `HttpContext.Items` → `Context` (read-only, no lookup).
- Behavioral regression: `TenantBehavior` rejects `TenantId == Empty` (unless bypass);
  `TenantRlsInterceptor` still emits `SET LOCAL app.current_tenant_id`; `AuthorizationBehavior`
  still receives `TenantId`.
- DI smoke test: building each service host must not throw (guards against the original
  missing-registration class of failures).

## 7. Rollback Strategy

- Each phase is independently compilable and deployable; Phases 0–1 are behavior-preserving.
- Compatibility aliases (Phase 2) remain until Phase 4, so old interfaces keep resolving
  during partial rollout.
- The Phase 5 single-impl cutover is the only high-risk step; keep per-service registrations
  until verified, then remove.
- No authentication/JWT/dead-provider changes, so rollback never affects tenant discovery or
  security boundaries.