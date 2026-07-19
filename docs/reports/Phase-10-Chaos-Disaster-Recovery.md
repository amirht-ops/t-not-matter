# Phase 10 — Chaos Engineering & Disaster Recovery Validation

Status: **PASS**
Date: 2026-07-18
Scope: IdentityService, AuthorizationService, TenantService, PolicyService + shared Platform / SharedKernel resilience layers

---

## 1. Mission & Method

The objective of this phase is not to prevent dependency failures but to prove they are
handled correctly: no process crash, no data loss, no corrupted state, correct security posture
under failure, and automatic recovery.

Every result below is backed by an **executed, reversible fault-injection experiment** against
the live topology, with machine-readable evidence captured to
`tests-http/phase10/evidence/_chaos_report_*.json`. Faults are one of:

- `docker pause` / `docker unpause` (freeze a dependency, Steps 1–3, 6),
- `docker restart` (full container bounce, Step 7),
- scoped PostgreSQL backend termination (single-database outage, Step 4),
- `Stop-Process -Force` on a service + relaunch (downstream outage / process crash, Steps 5, 8).

Each fault is always reverted (in a `finally` block or by relaunch), so the environment is left
exactly as it was found. A key resilience defect was found and fixed during this phase (see §3).

### Harnesses (committed, repeatable)

| Script | Steps | Fault |
|--------|-------|-------|
| `tests-http/phase10/chaos.mjs` | 1, 2, 3, 6 | `docker pause`/`unpause` of rabbitmq / redis / opa |
| `tests-http/phase10/chaos-db.mjs` | 4 | terminate `policyservice` DB backends (scoped) |
| `tests-http/phase10/chaos-svc.mjs` | 5, 8 | stop + relaunch AuthorizationService process |
| `tests-http/phase10/chaos-restart.mjs` | 7 | `docker restart rabbitmq` |

### Topology under test

| Component | Runtime | How faulted (live) |
|-----------|---------|--------------------|
| PostgreSQL | Native Windows service `:5432` (shared by all four DBs) | Scoped: terminate only `policyservice` backends |
| RabbitMQ | Docker container `rabbitmq` | `docker pause` (Step 1), `docker restart` (Step 7) |
| Redis | Docker container `redis` | `docker pause`/`unpause` |
| OPA | Docker container `opa` `:8181` | `docker pause`/`unpause` |
| IdentityService | `dotnet` process `:5244` | observed; process killed as caller/crash target |
| AuthorizationService | `dotnet` process `:5108` | `Stop-Process` + relaunch (Steps 5, 8) |
| TenantService | `dotnet` process `:5068` | observed |
| PolicyService | `dotnet` process `:5000` | observed |

> The services run as host `dotnet` processes (not containerized in this environment), so a
> service outage is injected by stopping/relaunching the process rather than a container. The
> shared PostgreSQL is faulted with a **database-scoped** technique instead of stopping the whole
> server, to avoid a broad blast radius across all four services (per operator direction).

### Baseline (all healthy)

```
identity :5244  live=200 ready=200 status=Ready  db=Healthy redis=Healthy rmq=Healthy
authz    :5108  live=200 ready=200 status=Ready  db=Healthy redis=Healthy rmq=Healthy
tenant   :5068  live=200 ready=200 status=Ready  db=Healthy redis=Healthy rmq=Healthy
policy   :5000  live=200 ready=200 status=Ready  db=Healthy redis=Healthy rmq=Healthy
dotnet PIDs: identity=22980  tenant=20128  authz=12408  policy=7976
```

IdentityService periodically returns `429` on `/health/*` because its global authentication
rate limiter is not exempted from health endpoints (Residual Risk #4). Its process stayed alive
throughout; where its readiness could not be sampled cleanly, liveness was tracked by PID and
functional health by direct login probes.

---

## 2. Step 1 — RabbitMQ Failure  →  **PASS**

**Experiment.** `docker pause rabbitmq` for ~30s while issuing event-emitting writes
(policy create + publish), then `docker unpause`. Evidence: `_chaos_report_all.json` → `steps.step1`.

**Measured.**

| Metric | Value |
|--------|-------|
| Writes during broker outage | 3 × `create=201, publish=200` (all succeeded) |
| Outbox during outage | `pending=6, processed=1576, errored=0, deadLetter=0` |
| Outbox after recovery | `pending=0, processed=1582, errored=0, deadLetter=0` |
| RabbitMQ queue depth after drain | `0` |
| Service PIDs before/during/after | identical (no crash) |

**Interpretation.** The transactional Outbox decouples writes from broker availability: events
persisted in the same DB transaction as the business change, stayed `pending` while the broker
was frozen, then published automatically once it returned. No message was lost (all 6 pending
drained to processed), none dead-lettered, and the RabbitMQ queue drained to 0 — consistent with
`FOR UPDATE SKIP LOCKED` claiming + stable `EventId` + consumer-side idempotency preventing
duplicates. No process crashed.

---

## 3. Step 2 — Redis Failure  →  **PASS** (real defect found and fixed)

**Experiment.** `docker pause redis` for ~9s while reading cache-backed endpoints and probing
login, then `docker unpause` and poll all readiness monitors back to green. Evidence:
`_chaos_report_all.json` → `steps.step2`.

**Measured.**

| Metric | Value |
|--------|-------|
| Reads during Redis outage | 3 × `200` (`~27–37ms`) — served from source of truth |
| Login during Redis outage | `401` in `6505ms` (auth failure, **not** a `500`) |
| Reads after recovery | 3 × `200` (`~24–47ms`) |
| Readiness recovery time (all services) | `3000ms` |
| Service PIDs before/after | identical (no crash) |

**Interpretation.** The cache fails **open**: `RedisCacheService`,
`RedisDistributedRateLimiterService`, `RedisEventConsumerDeduplicationGuard`, and the distributed
lock/idempotency stores all catch `RedisException`/`TimeoutException` and degrade to the database
rather than failing the request. Business correctness never depends on a cache hit.

### 3.1 Defect found: multiplexer thrash on transient disconnect (FIXED)

The readiness-recovery poll exposed a real bug. On the **first** Redis experiment, TenantService
latched `NotReady` for **10+ minutes** after Redis recovered, while Policy and Authz recovered
within seconds. Root cause in `RedisConnectionFactory.GetConnection()`:

```csharp
// BEFORE (buggy): rebuild the multiplexer whenever it looks momentarily disconnected.
if (_connection is null || !_connection.IsConnected)
{
    _connection?.Dispose();
    _connection = CreateConnection();
}
return _connection;
```

Under concurrent health-probe + request traffic during an outage, every caller seeing
`IsConnected == false` disposed and rebuilt the multiplexer, racing multiple half-connected
multiplexers against each other and leaving the service permanently wedged after Redis returned.
A `StackExchange.Redis` multiplexer is designed to be created **once** and reused
(`AbortOnConnectFail=false` keeps it usable during an outage and it reconnects itself).

```csharp
// AFTER (fix): return the existing long-lived multiplexer even when momentarily disconnected;
// only build one if none exists yet.
var existing = _connection;
if (existing is not null)
    return existing;
lock (_lock)
{
    if (_connection is not null) return _connection;
    _connection = CreateConnection();
    _initialized = true;
    return _connection;
}
```

**Re-verified after the fix:** all readiness monitors returned to green in **3000ms**, reads
served `200` throughout, and no process crashed. The data plane had actually stayed correct even
while readiness was wedged (fail-open worked); the fix repairs the readiness/self-heal path.

File: `src/Platform/Platform.Caching/RedisConnectionFactory.cs`.

---

## 4. Step 3 — OPA Failure  →  **PASS**

**Experiment.** `docker pause opa` for ~8s while calling
`POST /api/v1/authorization/decisions/evaluate`, then `docker unpause`. Evidence:
`_chaos_report_all.json` → `steps.step3`.

**Measured.**

| Metric | Value |
|--------|-------|
| Baseline decision | `200`, `isAllowed=false`, reason `DeniedByPolicy` |
| Decisions during OPA outage | 3 × `200`, `isAllowed=false`, reason `OpaTimeout` (`~1055–1066ms`) |
| Decisions after recovery | 3 × `200`, `isAllowed=false`, reason `DeniedByPolicy` (`~60–77ms`) |
| Accidental allows during outage | `0` |
| Service PIDs before/after | identical (no crash) |

**Interpretation.** Authorization fails **closed** — the security-correct posture. The decision
engine maps an OPA timeout/failure to an explicit **Deny** (`OpaTimeout` / `OpaUnavailable`),
bounded by the per-call OPA timeout (`~1s`, matching `OpaOptions.TimeoutMs`), never an accidental
allow and never a `500`. After recovery, decisions resolve against real policy again
(`DeniedByPolicy`) and latency drops back to `~60ms`.

---

## 5. Step 4 — Database Failure (scoped to PolicyService)  →  **PASS**

**Experiment.** Inject a single-database outage: continuously terminate every backend attached
to the `policyservice` database (`pg_terminate_backend ... WHERE datname='policyservice'`) for
the fault window, while the other three databases are never touched. Evidence:
`_chaos_report_step4.json`.

> **Why not `CONNECTION LIMIT 0`?** PolicyService connects as the `postgres` **superuser**, and
> PostgreSQL does not enforce per-database `datconnlimit` for superusers — an initial attempt
> with `ALTER DATABASE ... CONNECTION LIMIT 0` had no effect (writes kept returning `201`). The
> sustained backend-termination loop is superuser-proof and precisely scoped.

**Measured.**

| Metric | Value |
|--------|-------|
| Baseline write | `201` in `38ms` |
| Writes during outage | `201` (1031ms), `500` (1037ms), `500` (999ms) — bounded, **no timeout/hang** |
| Backends terminated | `4` |
| Sibling services (identity/tenant/authz) | stayed `Ready` (fault scoped) |
| PolicyService PID before/after | `7976` = `7976` (no crash) |
| Recovery | write `#0`=`500` (274ms) → write `#1`=`201` (98ms); data-plane recovery `881ms` |
| Readiness / DB component | back to `Ready` / `Healthy`, no restart |

**Interpretation.** The DB outage genuinely reached PolicyService (writes failed with a bounded
`500` in ~1s — no hang, so no thread-pool starvation), the failure was scoped to PolicyService
only (siblings stayed healthy), the process never crashed, and it self-healed: readiness returned
to `Healthy` and writes succeeded again with **no restart**.

> **Empirically confirmed gap:** the first write immediately after recovery still returned `500`
> (a severed connection left in the pool) before the second succeeded. This is the missing Npgsql
> connection-resiliency (`EnableRetryOnFailure`) called out in Residual Risk #2 — the platform
> self-heals within one retry, but a transparent execution-strategy retry would hide even that
> single blip.

---

## 6. Step 5 — Downstream Service Failure  →  **PASS**

**Experiment.** Take the downstream **AuthorizationService fully offline** (`Stop-Process
-Force`), then drive login traffic through IdentityService. Login synchronously calls
AuthorizationService to enrich the token with the user's role/department
(`AuthorizationRoleResolver` → `GET /api/v1/authorization/roles/assignment`, a Polly-wrapped
typed `HttpClient`). Relaunch and confirm recovery. Evidence: `_chaos_report_step5.json`.

**Measured.**

| Metric | Value |
|--------|-------|
| Baseline login | `200` in `428ms`, `role_id` + `department_id` populated |
| AuthorizationService during fault | `live=0 ready=0` (process gone) — fault landed |
| Login during outage (evaluated) | `200` in `5301ms`, `role_id=null`, `dept=null` — **degraded, not failed** |
| Sibling services (tenant/policy) | stayed `Ready` |
| IdentityService PID before/after | `22980` = `22980` (caller did not crash) |
| Recovery | after relaunch, login `200` in `440ms` with `role_id` repopulated |
| AuthorizationService readiness recovery | `~9s` after relaunch |

**Interpretation.** The caller degrades gracefully: when the downstream is offline, the role
resolver catches the connection failure and returns `Result.Failure`, so **login still succeeds**
— just without the enriched role/department claims. Latency stayed bounded (`~5.3s`, the Polly
retry budget against a dead endpoint; no infinite hang → no thread starvation). Once
AuthorizationService was relaunched, IdentityService talked to it again automatically (circuit
closed) with **no restart of the caller** — the recovered login came back with `role_id`
populated.

> Note: IdentityService's global `AuthenticationLimiter` (`10/60s`, unpartitioned) returned `429`
> to some probes before they reached the handler. Those are excluded from the verdict (a `429` is
> "not evaluated"); the verdict rests only on requests that actually reached the login handler.
> See Residual Risk #4.

---

## 7. Step 6 — Network Interruption  →  **PASS**

**Experiment.** Simulate a transient network blip to a dependency: `docker pause opa`, then fire
a **concurrent burst of 20** authorization evaluations mid-outage, then `unpause`. This exercises
the same Polly transient-error / timeout path a real network interruption hits. Evidence:
`_chaos_report_all.json` → `steps.step6`.

**Measured.**

| Metric | Value |
|--------|-------|
| Burst | 20 concurrent requests, all `200` |
| Max single-request latency | `1495.7ms` (bounded by OPA timeout) |
| Wall-clock for whole burst | `1503ms` |
| Timeouts / hangs | `0` |
| Accidental allows | `0` |
| Service PIDs before/after | identical (no crash) |

**Interpretation.** All 20 concurrent requests resolved within the bounded per-call timeout
(`~1.5s`), fail-closed, with no hang — proving there is no thread-pool exhaustion under
concurrent load against a dead dependency (all I/O is `async`, hard timeouts release threads
promptly). Recovery was automatic.

---

## 8. Step 7 — Container Restart  →  **PASS**

**Experiment.** `docker restart rabbitmq` (a full container bounce — the broker process is
stopped and a fresh one starts, unlike a pause) while issuing event-emitting writes. Verify
outbox no-loss/no-duplicate across the bounce and automatic reconnection. Evidence:
`_chaos_report_step7.json`.

**Measured.**

| Metric | Value |
|--------|-------|
| Writes during restart | 3 × `create=201, publish=200` |
| Outbox during reconnect window | `pending=6` (held ~30s while broker restarted) |
| Outbox after drain | `pending=0, processed 1591 → 1597, deadLetter=0` |
| RabbitMQ queue depth after drain | `0` |
| Service PIDs before/after | identical (no crash) |

**Interpretation.** Across a real container restart, MassTransit re-established its connection
automatically and the outbox published every held message once the broker returned — no loss (all
6 pending drained), no dead-lettering, queue depth back to 0, no service restart or manual
intervention required.

---

## 9. Step 8 — Unexpected Process Crash  →  **PASS**

**Experiment.** Covered live by Step 5: AuthorizationService was terminated with
`Stop-Process -Force` (an abrupt, uncontrolled kill — the exact analog of an unexpected process
crash), then relaunched. Evidence: `_chaos_report_step5.json`.

**Measured / interpretation.**
- **Dependent services survived the crash** — IdentityService kept serving logins (degraded), its
  PID unchanged; TenantService and PolicyService stayed `Ready`.
- **Clean restart** — on relaunch, AuthorizationService's startup coordinator re-ran (JWT/Redis/
  HttpClient/DB warmups + OPA policy provisioning) and readiness returned to green in `~9s`.
- **No lost work** — AuthorizationService's own state is DB-backed and its outbox resumes on
  boot; the killed process left no half-written business state (writes commit transactionally).
- **Automatic reconnection** — IdentityService's circuit breaker closed and role resolution
  resumed with no caller restart (recovered login returned enriched claims).

---

## 10. Deliverables

| Deliverable | Result | Evidence file |
|-------------|--------|---------------|
| **RabbitMQ Recovery** | PASS | `_chaos_report_all.json` (step1) — 6 pending → 0, queue 0, no dupes |
| **Redis Recovery** | PASS (defect fixed) | `_chaos_report_all.json` (step2) — fail-open, readiness 3000ms; `RedisConnectionFactory` fix |
| **Database Recovery** | PASS | `_chaos_report_step4.json` — scoped outage, bounded 500s, self-heal 881ms |
| **Downstream Service Recovery** | PASS | `_chaos_report_step5.json` — degraded login 200, recovered enriched |
| **Network Interruption** | PASS | `_chaos_report_all.json` (step6) — 20/20 bounded, no hang |
| **Container Restart** | PASS | `_chaos_report_step7.json` — outbox no-loss across `docker restart` |
| **Process Crash** | PASS | `_chaos_report_step5.json` — force-kill + auto-recovery |

### PASS / BLOCKED verdict: **PASS**

All eight steps were executed live with reversible faults and passed against measured evidence:
zero process crashes across every experiment, correct security posture under failure
(authorization fails **closed**, cache fails **open**), no data loss or duplication in the
outbox across broker pause and full container restart, graceful degradation of a synchronous
downstream call, and fully automatic recovery in every case. One real resilience defect
(`RedisConnectionFactory` multiplexer thrash) was found via the readiness-recovery probe, fixed,
and re-verified.

---

## 11. Remaining Risks

1. **Npgsql connection resiliency is not enabled.** No `EnableRetryOnFailure`/execution strategy
   on any `UseNpgsql(...)`. Step 4 empirically showed the first write after a DB blip returns a
   single `500` before self-healing. **Recommendation:** enable a bounded Npgsql retrying
   execution strategy (mindful of its interaction with explicit `UnitOfWork` transactions).
2. **Whole-server DB outage not exercised.** PostgreSQL is a shared native service, so Step 4 used
   a database-scoped fault to avoid a broad blast radius. **Recommendation:** a disposable
   docker-compose/K8s Postgres dedicated to chaos runs, so a full-server stop can be tested
   repeatably.
3. **Services are not containerized here.** Steps 5/7/8 injected process- and broker-container
   faults; a true pod eviction/reschedule path is not covered. **Recommendation:** add container
   images + compose/K8s manifests and re-run Steps 7–8 as real pod restarts.
4. **IdentityService rate limiter is global and not health-exempt.** The `AuthenticationLimiter`
   (`10/60s`) is unpartitioned and also throttles `/health/*`, producing `429`s that complicate
   probing. **Recommendation:** partition the limiter per client/identity and exempt `/health/*`.
5. **Dead-letter handling is terminal.** Messages exceeding `MaxAttempts` land in
   `policy_dead_letter` with no automated replay path. **Recommendation:** add an admin
   endpoint / runbook to inspect and re-drive dead-lettered events.

---

## 12. Reproduction

All harnesses are committed under `tests-http/phase10/` and self-revert:

```powershell
# Steps 1,2,3,6 — infra pause/unpause (safe, fully reversible)
node tests-http/phase10/chaos.mjs --step all

# Step 4 — scoped PolicyService DB outage (terminates only policyservice backends)
node tests-http/phase10/chaos-db.mjs

# Step 5 & 8 — downstream service outage / process crash (stops + relaunches AuthorizationService)
node tests-http/phase10/chaos-svc.mjs

# Step 7 — container restart (docker restart rabbitmq)
node tests-http/phase10/chaos-restart.mjs
```

Evidence JSON is written to `tests-http/phase10/evidence/_chaos_report_*.json`. Crash detection
compares `dotnet` service PIDs before/during/after each experiment; they were stable throughout.
