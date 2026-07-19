# Policy Service — Cache Strategy Report

- **Status:** Proposed (discovery, pre-implementation).
- **Date:** 2026-07-15
- **Depends on:** `00`, `01`, `05` (= this doc, cache), `07` (read models).
- **Authoritative inputs:** ADR-016 (caching ownership), ADR-014 (local hierarchy), reference `AuthorizationService.Infrastructure/Caching/RedisAuthorizationCache.cs`, `SharedKernel` `ICachedQuery`/`CachingBehavior`, `Platform` caching registration.

> **Purpose:** decide, per use case, what is cached, by whom, with what TTL, and how it is invalidated — under ADR-016 ownership rules and the hard constraint that the **hot-path consumption (UC-20) is never read-through-cached** (usage is high-churn, per-consumer, strongly consistent).

---

## 1. ADR-016 Ownership Summary

- Redis is **shared infrastructure**; business caches are **owned by the service** that produces the data.
- PolicyService owns its cache; it may **not** cache another service's data (so it never caches AuthorizationService responses — it hydrates a local read model instead, `04` §3).
- Cache reads are **fail-open**: a cache miss or Redis failure falls through to the source (DB/read model), never raises.
- Invalidation is event-driven; the owning service invalidates its own keys when its data changes.

---

## 2. What Is Cached (decisions)

| Use case | Cache? | Why |
|----------|--------|-----|
| UC-13 Resolve effective subscription | **Yes** | config-driven, low churn, read on every consumption (hot read) |
| UC-19 Resolve effective quota | **Yes** | config-driven, low churn, read on every consumption |
| UC-05/06/11/12/17/18 (config reads) | Optional | low volume; cache only if profiling shows pressure |
| UC-23 Allowance status (composite) | **Short TTL only (≤30s)** | computed from usage (high-churn) + quota (low-churn); must not mask live debt |
| **UC-20 Record consumption** | **No read cache** | mutates usage/debt; reading stale counters would double-count or miss debt blocks |
| UC-21/22 (raw usage/debt) | **No** | high-churn counters; always read from ledger |
| UC-26–29 hydration | n/a | the read model *is* the cache of hierarchy |

**Key principle:** cache **resolution results** (subscription/quota binding) and **config**, never **runtime counters**. This keeps the per-consumer strong-consistency boundary (`02` §3) intact.

---

## 3. Mechanism

- Implement `ICachedQuery` on the cached queries (UC-13, UC-19; optionally UC-23 with a tight TTL). `CachingBehavior` (enabled by default per `03`/Platform `BehaviorOptions.EnableCaching=true`) reads `CacheKey` + `AbsoluteExpirationRelativeToNow` and serves/store via `IDistributedCache`.
- Backing store: `AddStackExchangeRedisCache` (instance name e.g. `"policy"`) + `AddPlatformCaching` (same wiring as Authz `DependencyInjection.cs`).
- Key scheme via `CacheKeyBuilder`, namespaced per tenant:
  - `policy:{tenantId}:effective-subscription:{scopeHash}`
  - `policy:{tenantId}:effective-quota:{scopeHash}`
  - `policy:{tenantId}:allowance-status:{consumerId}:{action}` (TTL 30s)
- `RedisAuthorizationCache`-style guard: wrap `IDistributedCache` calls in try/catch → `null` on failure (fail-open). For `ICachedQuery` this means the behavior simply proceeds to the handler on a cache exception.

---

## 4. Invalidation

Invalidation is **event-driven**, not inline in the command handler (to keep handlers pure per ADR-015 — cache mutation is a side effect that must not be entangled with the transaction):

1. A `CacheInvalidationConsumer : IConsumer<EventEnvelope>` subscribes to PolicyService's own outbox events:
   - `policy.subscription-*` → invalidate `effective-subscription:*` for that scope/tenant.
   - `policy.quota-policy-*` → invalidate `effective-quota:*` for that scope/tenant.
   - `policy.policy-published.v1` / `policy.policy-archived.v1` → invalidate policy-derived resolution keys.
2. The consumer removes the affected keys from `IDistributedCache` (best-effort; failure is non-fatal).
3. UC-23 (allowance status) needs **no explicit invalidation** because its TTL ≤30s bounds staleness; it self-heals.

> Alternative considered: invalidate inside the command handler via an injected `IDistributedCache`. Rejected — it couples cache side-effects into the transactional handler and risks inconsistency if the tx rolls back after a cache eviction. The event-driven consumer keeps the write path clean.

---

## 5. TTL Recommendations

| Key | TTL | Rationale |
|-----|-----|-----------|
| effective-subscription | 5 min | binding changes are rare; brief staleness is harmless (re-resolved on next consumption window boundary) |
| effective-quota | 5 min | quota amendments rare |
| allowance-status | 30 s | bounded staleness on high-churn counters |
| config reads (optional) | 2 min | low volume |

All TTLs are **absolute** (not sliding) so a flurry of reads cannot pin a stale entry forever.

---

## 6. TODO

- [ ] Implement `ICachedQuery` on `ResolveEffectiveSubscriptionQuery` + `ResolveEffectiveQuotaQuery` (+ optional `GetAllowanceStatusQuery` with 30s TTL).
- [ ] Implement `CacheInvalidationConsumer` filtering PolicyService's own config events; register with MassTransit.
- [ ] Add `AddStackExchangeRedisCache` + `AddPlatformCaching` in `PolicyService.Infrastructure` `AddInfrastructure`.
- [ ] Decide whether UC-23 caching is worth the 30s-staleness trade-off vs. always computing (default: compute, no cache, unless profiling demands).

**Next:** `06-mediatr-pipeline-validation-report.md`.
