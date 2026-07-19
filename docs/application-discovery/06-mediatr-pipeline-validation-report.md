# Policy Service — MediatR Pipeline Validation Report

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`, `01`, `02`.
- **Authoritative inputs:** `Platform.Behaviors/BehaviorServiceCollectionExtensions.cs`, `Platform.Behaviors/BehaviorOptions.cs`; reference `AuthorizationService.Application/Features/GetEffectivePermissions/*` (validator + query + handler), `AuthorizationService.Application/DependencyInjection.cs`.

> **Purpose:** verify, against the actual `Platform.Behaviors` registration and the Authz vertical-slice template, that every use case is marked with the correct MediatR marker interfaces, has a FluentValidation validator, and obeys tenant/transaction/authorization rules. This is the gate that prevents "works locally, fails in pipeline" surprises.

---

## 1. Confirmed Behavior Set & Ordering

`AddPlatformBehaviors(configuration)` registers (outermost → innermost), each gated by `BehaviorOptions`:

```
TenantBehavior → ValidationBehavior → AuthorizationBehavior → RetryBehavior →
UnitOfWorkBehavior → [AuditBehavior if enabled] → LoggingBehavior →
PerformanceBehavior → IdempotencyBehavior → CachingBehavior → MetricsBehavior → FailClosedBehavior
```

`BehaviorOptions` **defaults** (from `BehaviorOptions.cs`):
`EnableAuthorization=true`, `EnableAudit=false`, `EnableUnitOfWork=true`, `EnablePerformanceWarnings=true`, `EnableIdempotency=true`, `EnableCaching=true`, `EnableMetrics=true`, `EnableRetry=true`, `EnableFailClosed=true`.

**Implications for PolicyService:**
- `UnitOfWorkBehavior` is ON → every `ITransactionalRequest` opens a tx (`02`). ✔
- `AuthorizationBehavior` is ON → every `IAuthorizableRequest` is enforced (fail-closed). ✔
- `AuditBehavior` is **OFF** → PolicyService must NOT rely on it; audit is outbox-driven (`04` §5). ✔ (matches ADR-017)
- `CachingBehavior` is ON → `ICachedQuery` queries are auto-cached (`05`). ✔
- `IdempotencyBehavior` is ON → `IIdempotentRequest` commands dedupe. ✔
- `FailClosedBehavior` is ON → authorization deny-by-default; **authorization rules must be defined** for every `IAuthorizableRequest` (`06` §5).

---

## 2. Marker-Interface Matrix (per use case)

Derived from `00` §4 and `01`. `C`=command, `Q`=query, `(J)`=job-triggered, `(E)`=consumer.

| UC | `ITransactionalRequest` | `IAuthorizableRequest` | `IIdempotentRequest` | `ICachedQuery` | `IRetryableRequest` |
|----|:--:|:--:|:--:|:--:|:--:|
| UC-01 Create policy (C) | ✔ | ✔ | ✔ | — | — |
| UC-02 Publish policy (C) | ✔ | ✔ | — | — | — |
| UC-03 Archive policy (C) | ✔ | ✔ | — | — | — |
| UC-04 Change condition (C) | ✔ | ✔ | — | — | — |
| UC-05/06 Policy reads (Q) | — | — | — | ✔ (opt) | — |
| UC-07 Assign sub (C) | ✔ | ✔ | ✔ | — | — |
| UC-08/09/10 Sub lifecycle (C) | ✔ | ✔ | — | — | — |
| UC-11/12 Sub reads (Q) | — | — | — | ✔ (opt) | — |
| UC-13 Resolve sub (Q) | — | — | — | ✔ | — |
| UC-14 Define quota (C) | ✔ | ✔ | ✔ | — | — |
| UC-15/16 Amend/Remove quota (C) | ✔ | ✔ | — | — | — |
| UC-17/18 Quota reads (Q) | — | — | — | ✔ (opt) | — |
| UC-19 Resolve quota (Q) | — | — | — | ✔ | — |
| **UC-20 Record consumption (C, system)** | ✔ | — | **✔** | — | **✔** |
| UC-21/22 Usage/Debt reads (Q) | — | — | — | — | — |
| UC-23 Allowance status (Q) | — | — | — | ✔ (30s) | — |
| UC-24 Recover debt (C, job) | ✔ | — | — | — | ✔ |
| UC-25 Reset (C) | ✔ | ✔ | ✔ | — | — |
| UC-26–29 Hydration (E→cmd) | ✔ (internal cmd) | — | ✔ | — | — |
| UC-30/31 OPA (E) | — | — | ✔ | — | ✔ |

Notes:
- **All commands are `ITransactionalRequest`**; **all queries are NOT** (`02` invariant).
- **UC-20 (consumption) is the exception to `IAuthorizableRequest`** — it is a system/feed operation authorized at the gateway, not by ABAC (`00` §4.4). Marking it authorizable would deny every consumption under `FailClosedBehavior`.
- UC-20 is `IIdempotentRequest` **and** `IRetryableRequest` because it is the hot path and must survive at-least-once redelivery + transient failures without double-recording (idempotency key = `(tenantId, consumerId, action, correlationId)` or a deterministic idempotency key from the incoming feed event).
- UC-24/UC-30/31 are retryable (idempotent by construction: recovery is min(allowance,debt); OPA push is PUT-idempotent).

---

## 3. FluentValidation (ValidationBehavior)

Every command/query gets a validator registered via `AddValidatorsFromAssemblyContaining<SomeHandler>()` (Authz pattern). Validators enforce **only transport-level** rules (non-empty ids, value-object parseability); **business rules live in the domain** (never duplicated in validators).

Example sketch (`RecordConsumptionCommandValidator`):
```csharp
public sealed class RecordConsumptionCommandValidator : AbstractValidator<RecordConsumptionCommand>
{
    public RecordConsumptionCommandValidator()
    {
        RuleFor(x => x.ConsumerId).NotNull();
        RuleFor(x => x.Action).Must(a => a is not null);
        RuleFor(x => x.Units).GreaterThanOrEqualTo(0);
    }
}
```
- Query validators (e.g. `ResolveEffectiveQuotaQueryValidator`) validate `Scope` non-null.
- Validators **never** carry `TenantId` — it comes from the accessor (`00` §3 ADR-012).

---

## 4. Tenant Isolation (TenantBehavior)

- `TenantBehavior` asserts `IRequestContextAccessor.Context.TenantId` is present and non-empty; it fails closed otherwise.
- **No command or query may declare a `TenantId`/`RequestContext` property** (ADR-012). The `RecordConsumptionCommand` carries only `ConsumerId, ActionKey, Units, ResourceScope` — **not** tenant. Verified against `00` §4 definitions. ✔
- Consumers (UC-26–31) are `IConsumer<EventEnvelope>`, not MediatR requests, so `TenantBehavior` does not apply to them; they read `tenantId` from the envelope and pass it into the internal command explicitly.

---

## 5. Authorization (AuthorizationBehavior + FailClosed)

- `EnableFailClosed=true` ⇒ any `IAuthorizableRequest` without a matching policy is **denied**.
- Every **config/admin** mutating command (UC-01..04, 07..10, 14..16, 25) is `IAuthorizableRequest` with `(Action, Resource)` describing the capability (e.g. `("policy:publish", "policy/{id}")`). UC-20 is deliberately excluded (system operation, `00` §4.4).
- **Action required before implementation:** define the PolicyService authorization policy set (actions/resources) and ensure the ABAC policy store (AuthorizationService) has rules granting them to authorized callers. This is an external dependency — TODO track it.
- Queries and recovery/OPA consumers are **not** `IAuthorizableRequest` (internal/operational).

---

## 6. Self-Check Gate

- [x] `AddPlatformBehaviors(configuration)` present in Api `Program.cs`.
- [x] `AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<XHandler>())` + `AddValidatorsFromAssemblyContaining<XHandler>()` in `PolicyService.Application` `AddApplication`.
- [x] No command carries `TenantId`.
- [x] Every command = `ITransactionalRequest`; every query ≠.
- [x] `AuditBehavior` OFF acknowledged → audit via outbox only.
- [ ] Authorization policy set defined (external TODO).
- [ ] Idempotency key scheme for UC-20 finalized.

**Next:** `07-read-models-and-projections-report.md`.
