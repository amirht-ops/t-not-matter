# عملکرد و مقیاس‌پذیری — پلتفرم امنیت سازمانی

---

## ۱. معماری عملکرد

### MediatR Pipeline Behaviors

هر درخواست از ۱۱ رفتار عبور می‌کند. رفتارها از بیرونی‌ترین به درونی‌ترین اجرا می‌شوند:

| رفتار | تأثیر عملکرد | بهینه‌سازی |
|--------|-------------|-----------|
| MetricsBehavior | حداقل | Histogram + counter فقط |
| CachingBehavior | مثبت | کاهش queries تا ۹۰٪ |
| RetryBehavior | منفی | فقط در خطا، max 3 retries |
| IdempotencyBehavior | حداقل | Redis atomic check |
| PerformanceBehavior | حداقل | فقط logging اگر > threshold |
| LoggingBehavior | حداقل | Structured logging |
| AuditBehavior | حداقل | Fire-and-forget |
| UnitOfWorkBehavior | متوسط | Transaction wrapping |
| AuthorizationBehavior | متوسط | HTTP call to AuthorizationService |
| ValidationBehavior | حداقل | Parallel FluentValidation |
| TenantBehavior | حداقل | In-memory check |

### Caching Strategy

#### Redis Cache Layers

| لایه | TTL | توضیح |
|------|-----|-------|
| Effective Permissions | ۵ دقیقه | Version-based invalidation |
| Tenant Slug | ۵ دقیقه | Cache-aside pattern |
| Tenant Status | ۵ دقیقه | Event-driven invalidation |
| User Session | ۵ دقیقه | Cache-aside |
| Authorization Decision | ۵ ثانیه | ADR-005 |

#### Cache Invalidation

Invalidation event-driven از طریق MassTransit consumers:
- `authorization.cache` — ۱۵+ نوع رویداد
- `tenant.cache` — ۵ نوع رویداد (TenantCreated, TenantStatusChanged, TenantPlanUpgraded, DepartmentCreated, DepartmentStatusChanged)

#### Distributed Locking

Redis distributed lock با Lua script برای انتشار اتمیک:
```lua
if redis.call("get", KEYS[1]) == ARGV[1] then
    return redis.call("del", KEYS[1])
end
return 0
```

---

## ۲. عملکرد Database

### AsNoTracking

تمام queries فقط-خواندنی از `AsNoTracking()` استفاده می‌کنند:
- کاهش memory footprint
- کاهش change tracking overhead
- بهبود performance برای read-heavy workloads

### Query Optimization

| بهینه‌سازی | وضعیت | توضیح |
|-----------|--------|-------|
| AsNoTracking | ✅ | تمام read-only queries |
| Pagination | ✅ | PolicyRepository.GetByStatusAsync, GetByDomainAsync |
| Select Projections | ✅ | AuthorizationService (PermissionRepository, RoleAssignmentRepository) |
| Composite Primary Keys | ✅ | {TenantId, Id} — بهبود RLS performance |
| Global Query Filters | ✅ | EF Core automatic tenant filtering |

### N+1 Query Analysis

هیچ الگوی N+1 query پیدا نشد. RoleAssignmentRepository hierarchy walk (۲ query + in-memory processing) یک انتخاب طراحی عمدی است.

### Outbox Processing

```sql
-- FOR UPDATE SKIP LOCKED
SELECT * FROM OutboxMessages 
WHERE ProcessedAt IS NULL 
ORDER BY CreatedAt 
LIMIT 10
FOR UPDATE SKIP LOCKED;
```

- همزمانی بالا برای multiple worker instances
- جلوگیری از double-processing
- Dead-letter برای پیام‌های معیوب

---

## ۳. عملکرد Messaging

### RabbitMQ Configuration

| تنظیم | مقدار | توضیح |
|--------|-------|-------|
| Prefetch Count | ۱۰ | جلوگیری از overload |
| Concurrency Limit | ۸ | همزمانی consumer |
| Retry Policy | Exponential(10, 1s, 30s, 5s) | ۱۰ تلاش |
| Dead Letter Queue | هر endpoint | `.dlx` / `.dlq` |

### Message Flow

```
Domain Event → OutboxProcessor → RabbitMQ → Consumer
                (FOR UPDATE        (Exponential
                 SKIP LOCKED)       Retry)
```

### Throughput Estimates

| سناریو | تخمین |
|--------|-------|
| Authorization checks/sec | ~۱۰۰۰ (با cache) |
| Outbox processing/sec | ~۵۰۰ |
| Event publishing/sec | ~۱۰۰۰ |
| Cache invalidation/sec | ~۵۰۰ |

---

## ۴. عملکرد OPA

### OPA Integration

```
PolicyService → OpaSyncService → HTTP PUT/DELETE → OPA
AuthorizationService → OpaPolicyEvaluationGateway → HTTP POST → OPA
```

### OPA Performance

| متریک | مقدار |
|--------|-------|
| Average evaluation time | ~۱۰ms |
| Timeout | ۵ ثانیه |
| Circuit breaker threshold | ۵ خطای متوالی |
| Circuit breaker timeout | ۳۰ ثانیه |

### OPA Caching

OPA policies on startup sync:
- `OpaStartupSyncService` — pull-based on startup
- `OpaSyncFallbackProcessor` — circuit breaker wrapping IOpaSyncService

---

## ۵. مقیاس‌پذیری

### Horizontal Scaling

| کامپوننت | استراتژی |
|----------|---------|
| API Services | Stateless, HPA ready |
| PostgreSQL | Read replicas + connection pooling |
| Redis | Cluster mode |
| RabbitMQ | Quorum queues |
| OPA | Multiple instances behind load balancer |

### Tenant Isolation Performance

#### PostgreSQL RLS

```sql
-- هر query خودکار filter می‌شود
SET LOCAL app.current_tenant_id = '{tenantId}';
-- سیاست RLS اعمال می‌شود
SELECT * FROM table WHERE tenant_id = current_setting('app.current_tenant_id')::uuid;
```

**تأثیر:** ~۲-۵ms overhead per query

#### EF Core Global Query Filters

```csharp
// خودکار در هر query
HasQueryFilter(e => e.TenantId == _currentTenantId && !e.IsDeleted);
```

**تأثیر:** ~۰ms (ترجمه به SQL)

### Connection Pooling

PostgreSQL connection pooling از طریق Npgsql:
- Min connections: ۵
- Max connections: ۱۰۰
- Connection idle lifetime: ۳۰۰ ثانیه

---

## ۶. bottleneck های شناسایی شده

### bottleneck 1: Authorization Check Latency

**مسیر:** Service → AuthorizationServiceClient → AuthorizationService → OPA
**تأثیر:** ~۵۰-۱۰۰ms per authorization check
**راه‌حل:** Cache effective permissions (۵ دقیقه TTL)

### bottleneck 2: Outbox Processing

**مسیر:** Domain Event → OutboxTable → OutboxProcessor → RabbitMQ
**تأثیر:** ~۱۰۰ms latency per event
**راه‌حل:** FOR UPDATE SKIP LOCKED + batch processing

### bottleneck 3: Cache Invalidation Storm

**مسیر:** Role change → ۱۵+ cache invalidation events
**تأثیر:** ~۵۰۰ms burst
**راه‌حل:** Batch invalidation, debouncing

### bottleneck 4: Tenant Service Slug Lookup

**مسیر:** API → TenantRepository → Database
**تأثیر:** ~۱۰ms per lookup
**راه‌حل:** Redis caching (۵ دقیقه TTL)

---

## ۷. معیارهای عملکرد (Performance Metrics)

### MediatR Metrics

```csharp
// MetricsBehavior.cs
private static readonly Meter Meter = new("EnterpriseAuthPlatform");
private static readonly Histogram<double> RequestDuration = 
    Meter.CreateHistogram<double>("mediatr.request.duration", "ms");
private static readonly Counter<long> RequestCount = 
    Meter.CreateCounter<long>("mediatr.request.count");
```

### Custom Metrics

| متریک | نوع | توضیح |
|--------|-----|-------|
| `mediatr.request.duration` | Histogram | مدت زمان handler |
| `mediatr.request.count` | Counter | تعداد درخواست‌ها |
| `outbox.processing.duration` | Histogram | مدت زمان پردازش outbox |
| `cache.hit.rate` | Counter | نرخ cache hit |
| `authorization.evaluation.duration` | Histogram | مدت زمان ارزیابی |

---

## ۸. توصیه‌های بهینه‌سازی

### کوتاه‌مدت

1. **Add rate limiting** — rate limit برای login endpoint (HIGH-10)
2. **Batch cache invalidation** — کاهش cache storm
3. **Connection pooling tuning** — بهینه‌سازی pool size

### میان‌مدت

1. **Read replicas** — جداسازی read/write traffic
2. **CDN for static assets** — کاهش بار API
3. **Async audit logging** — fire-and-forget audit

### بلندمدت

1. **Event sourcing** — برای Audit aggregate
2. **CQRS read model** — materialized views for complex queries
3. **GraphQL** — flexible query for reporting
