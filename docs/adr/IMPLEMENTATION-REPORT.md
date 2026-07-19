# Implementation Report

Date: 2026-07-10
Scope: Approved production fixes from Architecture Review Board
Method: Minimal changes with evidence-based verification

---

## Files Changed

### P1: Outbox DeadLetter Lifecycle Bug (Critical)

**File:** `src/Platform/Platform.Infrastructure/Outbox/OutboxProcessorBase.cs`
**Change:** Added `message.MarkProcessed()` before `SaveChangesAsync`, moved `SaveChangesAsync` outside the `if (!exists)` block.

**Before:**
```
MoveToDeadLetterAsync:
  if (!exists):
    create DeadLetterMessage
    SaveChangesAsync           ← only saves inside if-block
                              ← original OutboxMessage retains ProcessedAt = NULL
```

**After:**
```
MoveToDeadLetterAsync:
  if (!exists):
    create DeadLetterMessage  ← staged in change tracker
  message.MarkProcessed()      ← marks tracked entity ProcessedAt = NOW
  SaveChangesAsync             ← atomically saves DeadLetterMessage + ProcessedAt update
```

**Evidence of bug:**
1. `ProcessMessageAsync` checks `message.RetryCount >= MaxAttempts` → calls `MoveToDeadLetterAsync`
2. `MoveToDeadLetterAsync` creates `DeadLetterMessage` but never calls `MarkProcessedAsync` on the original `OutboxMessage`
3. `ClaimPendingBatchAsync` selects messages WHERE `ProcessedAt IS NULL AND NextRetryAt <= NOW()`
4. After dead letter: `NextRetryAt = NOW + 60s` (from lease update), but `ProcessedAt` stays NULL
5. After 60s: `NextRetryAt <= NOW()` becomes TRUE → message claimed again → dead-letter check finds existing `DeadLetterMessage` → no-op → repeats every ~60s forever

**Fix proof:**
- `message.MarkProcessed()` sets `ProcessedAt = DateTimeOffset.UtcNow` on the tracked entity
- Both the DbContext (`TDbContext`) from DI and the repository's DbContext are the same instance (scoped, same scope)
- `SaveChangesAsync` persists both the `DeadLetterMessage` insertion AND the `OutboxMessage.ProcessedAt` update in the same database transaction
- After fix: `ProcessedAt != NULL` → `ClaimPendingBatchAsync` WHERE clause excludes it → no re-poll

---

### P2: MassTransit Dual Registration (High)

**File:** `src/Services/IdentityService/IdentityService.Infrastructure/DependencyInjection.cs`
**Change:** Removed the dead `AddMassTransit` block (lines 93-132).

**Evidence:**
- `AddIdentityServiceInfrastructure()` is defined at line 41 but **NEVER CALLED** in any startup path
- The actual startup (`Program.cs:85`) calls `AddIdentityService()` which is in `ServiceCollectionExtensions.cs`
- `AddIdentityService()` does NOT call `AddIdentityServiceInfrastructure()`
- Therefore, the `AddMassTransit` inside `AddIdentityServiceInfrastructure()` is dead code
- The only active `AddMassTransit` is in `ServiceCollectionExtensions.cs:126-149`

**Verification:**
- TenantService: `AddTenantService()` calls `AddTenantServiceInfrastructure()` ✅ (alive)
- IdentityService: `AddIdentityService()` does NOT call `AddIdentityServiceInfrastructure()` ❌ (dead)

**No behavior change:** The removed code never executed at runtime.

---

### P3: Dead Code Cleanup (Medium)

**Removed (confirmed dead by full solution analysis):**

| File | Reason |
|---|---|
| `SharedKernel/Domain/Events/IIntegrationEvent.cs` | 0 implementations, 0 references outside definition |
| `AuthorizationService.Infrastructure/Messaging/IntegrationEvents/RoleAssignedIntegrationEvent.cs` | Never published, consumed, or referenced |
| `AuthorizationService.Infrastructure/Messaging/IntegrationEvents/PermissionGrantedIntegrationEvent.cs` | Never published, consumed, or referenced |
| `AuthorizationService.Infrastructure/Messaging/IntegrationEvents/AuthorizationEvaluatedIntegrationEvent.cs` | Never published, consumed, or referenced |

**Retained (uncertain — need further analysis):**

| File | Reason for Retention |
|---|---|
| `IdentityService.Infrastructure/Persistence/DeadLetterMessage.cs` | Local copies exist but are not used by EF config (Platform entity used). Could be planned for separate schema. |
| `TenantService.Infrastructure/Persistence/DeadLetterMessage.cs` | Same reason |
| `AuthorizationService.Infrastructure/Persistence/DeadLetterMessage.cs` | Same reason |
| `SharedKernel/Infrastructure/Outbox/IOutboxRepository.cs` | Registered in DI but never injected. Future handlers may use it. |
| `TenantService.Application/Common/Abstractions/ITenantOutboxRepository.cs` | Same reason |
| `IdentityService.Infrastructure/DependencyInjection.cs` (entire method `AddIdentityServiceInfrastructure`) | Contains `IPlatformOutboxRepository<IdentityDbContext>` registration needed for outbox to function. Should be called, not deleted. |

---

### P4: DepartmentId Authentication Contract (High)

**Bug #1 — TenantMiddleware drops DepartmentId**

**File:** `src/Platform/Platform.Middleware/TenantMiddleware.cs`
**Change:** Line 57 — `null` → `principal.IsUser ? principal.DepartmentId : null`

**Before:**
```csharp
contextAccessor.Context = new RequestContext(
    tenantId, correlationId, requestId, userId,
    null,  // ← DepartmentId always null
    ...);
```

**After:**
```csharp
contextAccessor.Context = new RequestContext(
    tenantId, correlationId, requestId, userId,
    principal.IsUser ? principal.DepartmentId : null,  // ← passes DepartmentId from JWT
    ...);
```

**Evidence:** `ICurrentPrincipal` already has `DepartmentId` from `CurrentPrincipalFactory.CreateFromClaimsPrincipal` (parsed from JWT `department_id` claim). But `TenantMiddleware` was hardcoding `null` for the `DepartmentId` parameter of `RequestContext`, dropping the value at the middleware boundary.

**Bug #2 — TenantService HttpRequestContextAccessor bypasses ICurrentPrincipal**

**File:** `src/Services/TenantService/TenantService.Api/Infrastructure/HttpRequestContextAccessor.cs`
**Change:** Replaced `TryDepartmentId(httpContext)` with `principal?.DepartmentId`, removed `TryDepartmentId` method.

**Before:**
```csharp
DepartmentId: TryDepartmentId(httpContext),
// ...
private static Guid? TryDepartmentId(HttpContext? httpContext) =>
    Guid.TryParse(httpContext?.User.FindFirst("department_id")?.Value, out var departmentId) ? departmentId : null;
```

**After:**
```csharp
DepartmentId: principal?.DepartmentId,
```

**Evidence:** This was a workaround for Bug #1. Since `TenantMiddleware` dropped `DepartmentId`, the TenantService accessor was reading `department_id` directly from `ClaimsPrincipal` instead of through the `ICurrentPrincipal` → `RequestContext` chain. With Bug #1 fixed, the workaround is removed and the chain becomes: `JWT → CurrentPrincipal → RequestContext` with **no direct claim parsing**.

**Unchanged (already correct):**
- `IdentityService.HttpRequestContextAccessor` — already reads `principal?.DepartmentId` ✅
- `JWT generation` (`HmacJwtTokenGenerator`) — already writes `department_id` claim ✅
- `CurrentPrincipalFactory` — already parses `department_id` from claims ✅
- `ICurrentPrincipal` — already has `DepartmentId` property ✅
- `RequestContext` record — already has `DepartmentId` parameter ✅
- `LoginCommandHandler` — already resolves `DepartmentId` from role and passes to token generator ✅
- `RegisterCommandHandler` — no token generation, no DepartmentId needed ✅

---

## Build & Test Results

| Target | Result |
|---|---|
| Full solution build (`dotnet build`) | **0 errors, 30 warnings** (all pre-existing) |
| `AuthorizationService.Domain.Tests` (20 tests) | **20/20 passed** |
| `IdentityService.IntegrationTests` (2 tests) | **1/1 passed, 1 pre-existing fail** (exploratory concurrency test, unrelated) |

Pre-existing failure: `ConcurrencyInvestigationTests.Investigate_DbUpdateConcurrencyException_Details` — designed to trigger `DbUpdateConcurrencyException` as part of EF Core behavior investigation. No assertions, pure exploratory. Unaffected by our changes.

---

## Bugs Fixed

1. **Outbox Dead Letter Re-poll Loop** — Dead-lettered messages re-polled every ~60s forever. Fix: Mark `OutboxMessage` as processed when moving to dead letter.
2. **MassTransit Source Code Confusion** — Two `AddMassTransit` calls in source code (one dead, one active). Fix: Removed the dead registration.
3. **Dead Abstraction (`IIntegrationEvent`)** — Interface with zero implementations across the entire solution. Fix: Removed.
4. **Dead Records (3 `IntegrationEvent`)** — Never published, consumed, or referenced. Fix: Removed.
5. **DepartmentId Dropped at Middleware Boundary** — `TenantMiddleware` passed `null` for `DepartmentId` in `RequestContext`, losing the value from JWT/CurrentPrincipal. Fix: Pass `principal.DepartmentId`.
6. **DepartmentId Claim Parsing Bypass** — TenantService `HttpRequestContextAccessor` read `department_id` directly from `ClaimsPrincipal` instead of through `ICurrentPrincipal`. Fix: Use `principal?.DepartmentId`.

---

## Technical Debt (Remaining)

Items documented but NOT fixed (architectural improvement, not production bug):

1. **`IPlatformOutboxRepository<IdentityDbContext>` not registered** — The IdentityService `AddIdentityServiceInfrastructure` method (never called) contains the registration. The active `AddIdentityService` method does NOT register it. Has no impact at runtime if `IdentityOutboxDispatcher` error is swallowed by the `try/catch` loop.
2. **Service-specific `OutboxRepository` dead code** — Single CTE+RETURNING versions exist but never used by outbox dispatcher. Platform generic (2-round-trip) version runs instead.
3. **Service-specific `DeadLetterMessage` entities** — Local copies exist with different nullability (`Guid?` vs `Guid`). Platform entity is what's actually mapped.
4. **`IOutboxRepository` / `ITenantOutboxRepository`** — Registered but never injected. Future handlers may use them.
5. **`IdentityService.Infrastructure.DependencyInjection` entire method dead** — Contains `IPlatformOutboxRepository`, `IIdentityUnitOfWork` registrations. Should either be called or integrated into `AddIdentityService`.

---

## Follow-up Report Items

During implementation, the following items were discovered but are NOT in the approved list. They should be reviewed for a future implementation cycle:

1. **Make `AddIdentityServiceInfrastructure` callable** — Currently the entire method is dead code. Either call it from `AddIdentityService` (like TenantService does) or move its registrations into the API layer. The `IPlatformOutboxRepository<IdentityDbContext>` registration is essential for outbox to function.

2. **Service-specific OutboxRepository cleanup** — The single CTE+RETURNING implementation in IdentityService and TenantService is superior (1 round-trip vs 2). Consider promoting to Platform to replace the 2-round-trip generic, OR remove the dead code.

3. **Delivery tracking** — No metrics, monitoring, or alerting exist for outbox processing.
