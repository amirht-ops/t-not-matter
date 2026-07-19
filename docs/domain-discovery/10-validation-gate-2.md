# PolicyService.Domain — Validation Gate 2 Report

**Date:** 2026-07-15
**Scope:** Phase F (aggregates) + Phase G (behaviors) complete. Full Domain layer implemented per
ADR-018, the Domain Discovery reports, the Aggregate/Invariant/Event/Pipeline documents, and the
conventions of `AuthorizationService.Domain` / `IdentityService.Domain` / `SharedKernel` / `Platform`.

## Build result
- `dotnet build PolicyService.Domain.csproj` → **succeeded, 0 errors, 0 warnings**.
- `dotnet build PolicyService.Api.csproj` (transitive chain) → **succeeded** (0 errors; only the
  pre-existing `NU1903` from `Microsoft.OpenApi` in the original Api csproj, unrelated to this work).

## Implemented surface
- **5 aggregates:** `Policy`, `Subscription`, `QuotaPolicy`, `UsageLedger`, `DebtLedger` — each with
  `private ctor` + `static Create(...) → Result<T>` + invariants + `RaiseDomainEvent`.
- **26 Value Objects / IDs:** principal + aggregate id VOs, `Quota`, `QuotaLimit`, `UsageCounter`,
  `DebtAmount`, `RecoveryPosition`, `SubscriptionScope`, `PolicyCondition`, `PolicyExpression`,
  `PolicyPriority`, `RegoModule`, `ActionKey`, `ConsumedUnits`, `ConsumptionDecision`, `ResourceType`,
  `ResourceId`.
- **20 domain events** (base `PolicyDomainEvent` + 19 sealed records), `EventTypeName = "policy.*.v1"`,
  carrying `TenantId` + `CorrelationId` only.
- **Contracts:** 5 repository interfaces, 4 specification interfaces, 8 service interfaces, 5 factory
  interfaces, enums, constants, errors.
- **Behaviors (Phase G):** `AllowanceEngine` (consumption: debt resolution → decision → usage
  recording → debt update), `RecoveryProcessor` (+ `DebtLedger.Recover`, debt-first),
  `AllowanceAdministrationService` (reset), `SubscriptionResolver` / `QuotaResolver` (+ precedence
  specs, User→Role→Tenant), `RegoGenerationService` / `PolicyCompiler` / `PolicyEvaluator` (no quota
  in Rego).

## Purity checks (Gate 2)
| Check | Result | Evidence |
|-------|--------|----------|
| Compile, 0 warnings/errors | ✅ PASS | Domain + chain build clean. |
| Aggregate size / single responsibility | ✅ PASS | Each aggregate covers one concept; ledgers hold per-(action,window) data. |
| Aggregate boundaries (Vernon) | ✅ PASS | Aggregates reference each other only by id/VO (Subscription→PolicyId, QuotaPolicy→Scope, ledgers→ConsumerId). No aggregate embeds another. |
| Invariant enforcement | ✅ PASS | All 20 invariants encoded (non-negative allowance/debt, consumption-always-succeeds, excess→debt, debt survives rollover, recovery debt-first, monthly dominance, reset≠config, hierarchy precedence, no RequestContext, no Rego quota). |
| Transaction boundaries | ✅ PASS | Engine mutates `UsageLedger`+`DebtLedger` together in memory; caller persists atomically (ADR-015). Resolution stages are reads. |
| No cross-aggregate writes | ✅ PASS | Aggregates never call each other; coordination is via domain services only. |
| No infrastructure leakage | ✅ PASS | Grep for EF/MediatR/ASP.NET/caching/logging/serialization/config/HttpClient/RequestContext found **no usage** (only a comment). |
| No `RequestContext` | ✅ PASS | Events carry TenantId+CorrelationId only (ADR-012). |
| No DTOs / EF attributes / MediatR / HTTP | ✅ PASS | None present. |
| No caching / logging / serialization / configuration | ✅ PASS | None present. Pure Domain only. |
| Pipeline fit (ADR-018 §20) | ✅ PASS | Every stage maps to an aggregate method / domain service / event. |
| No primitive obsession | ✅ PASS | External ids and domain measures are VOs; no raw Guid/long except inside VOs. |

## Notes
- `RegoModule` uses `System.Security.Cryptography.SHA256` to hash its source for integrity. This is BCL
  hashing, not infrastructure leakage (no EF/MediatR/ASP.NET/serialization of domain state).
- `ResourceType` / `ResourceId` / `PolicyPriority.Default` are valid domain VOs from the discovery; reserved
  for OPA resource targeting / future use.

## Conclusion
Gate 2 **PASSED**. The `PolicyService.Domain` layer is complete, production-grade, and consistent with
every bounded context. Implementation may proceed to the Application / Infrastructure / Api layers in a
subsequent phase (outside this Domain-only effort).
