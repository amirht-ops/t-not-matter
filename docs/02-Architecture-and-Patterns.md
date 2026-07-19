# معماری و الگوها — پلتفرم امنیت سازمانی

---

## ۱. معماری Clean Architecture

پلتفرم از معماری Clean Architecture با قانون وابستگی ۴ لایه استفاده می‌کند:

```
Domain ↓
Application ↓
Infrastructure ↓
Api
```

### قوانین وابستگی

| مجاز | ممنوع |
|------|-------|
| Domain → هیچ وابستگی بیرونی ندارد | Api → Domain (جهش مستقیم) |
| Application → Domain | Infrastructure → Api |
| Infrastructure → Application | Domain → Infrastructure |
| Api → Infrastructure | Domain → Api |

### ساختار هر سرویس

هر سرویس از ساختار یکسانی پیروی می‌کند:

```
Services/{ServiceName}/
├── {Service}.Domain.csproj           # صفر وابستگی
│   └── Domain/
│       ├── Aggregates/               # ریشه‌های Aggregate
│       ├── Events/                   # رویدادهای domenی
│       ├── ValueObjects/             # اشیاء ارزشی غیرقابل تغییر
│       ├── Repositories/             # فقط interface ها
│       └── Services/                 # interface های سرویس domenی
│
├── {Service}.Application.csproj      # فقط به Domain وابسته
│   └── Application/
│       ├── Features/                 # اسلایس‌های عمودی
│       └── Common/
│           ├── Abstractions/         # IUnitOfWork و غیره
│           └── Behaviors/            # رفتارهای pipeline MediatR
│
├── {Service}.Infrastructure.csproj   # به Application وابسته
│   └── Infrastructure/
│       ├── Persistence/              # EF Core، Repository
│       ├── Messaging/                # Publisher و Consumer
│       ├── Outbox/                   # OutboxProcessor
│       └── Caching/                  # Redis
│
├── {Service}.Api.csproj              # به Application وابسته (هرگز مستقیم به Infrastructure)
│   └── Api/
│       ├── Endpoints/                # Minimal API endpoint groups
│       ├── Middleware/                # CorrelationId، Tenant، Exception
│       └── Extensions/               # ServiceCollectionExtensions
│
└── {Service}.Tests.csproj
    └── Tests/
        ├── Unit/Domain/              # تست‌های invariant Aggregate
        ├── Unit/Application/         # تست‌های Handler با mocked infra
        ├── Integration/              # تست‌های endpoint با DB واقعی
        ├── Contract/                 # تست‌های contract
        └── E2E/                      # جریان‌های حیاتی مجوزدهی
```

---

## ۲. Domain Driven Design (DDD)

### Aggregate Root

تمام ۱۵ Aggregate از `AggregateRoot` در SharedKernel ارث‌بری می‌کنند:

```csharp
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
    
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

### Entity Base

```csharp
public abstract class Entity
{
    public Guid Id { get; private init; }
    public Guid TenantId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public int Version { get; private set; }
    public bool IsDeleted { get; private set; }
}
```

### اصول DDD رعایت شده

| اصل | وضعیت | توضیح |
|------|--------|-------|
| Encapsulation | رعایت شده | تمام Aggregate از `private set`/`private init` استفاده می‌کنند |
| Invariants | رعایت شده | تمام factory ها ورودی را اعتبارسنجی می‌کنند |
| Domain Events | رعایت شده | تمام تغییرات state رویداد domenی تولید می‌کنند |
| Factory Methods | رعایت شده | تمام Aggregate از static factory method استفاده می‌کنند |
| Value Objects | رعایت شده | غیرقابل تغییر، equality-by-value |
| Anemic Domain | جلوگیری شده | صفر مدل anemic |

### لیست Aggregate ها

| سرویس | Aggregate | ویژگی‌های کلیدی |
|--------|-----------|-----------------|
| Identity | User | DepartmentId, Email, PasswordHash, Status, MfaSettings, FailedLoginAttempts |
| Identity | Session | UserId, RefreshTokenHash, ExpiresAt, RevokedAt, IpAddress, UserAgent |
| Authorization | Role | Name, Description, Status, ParentRoleId, HierarchyDepth (حداکثر ۵) |
| Authorization | Permission | Key, Action, ResourceType, Lifecycle (6 مرحله), Version |
| Authorization | RoleAssignment | SubjectId, RoleId, AssignedBy, RevokedAtUtc |
| Authorization | PermissionGrant | RoleId, PermissionId, GrantedBy, RevokedAtUtc |
| Authorization | Department | Name, Description, _members |
| Authorization | UsageTracking | SubjectId, Action, ResourceType, Window, Count |
| Policy | Policy | Name, Category, Domain, Status, _rules, _versions (حداکثر ۲۰۰ rule) |
| Policy | Decision | PolicyId, Outcome, ReasonCode, EvaluationDurationMs |
| Tenant | Tenant | Code, Name, Slug, Status, PlanTier |
| Tenant | Department | Name, Description, Status |
| Audit | AuditRecord | EventType, Category, Action, Resource, Outcome, Source |
| Audit | SecurityEvent | EventType, Severity, Location, IsBlocked |
| Audit | ComplianceEvent | EventType, Regulation, PolicyId, ConsentId |

---

## ۳. CQRS (Command Query Responsibility Segregation)

### ساختار یک Feature

هر Feature یک پوشه عمودی مستقل است:

```
Features/{FeatureName}/
├── {Feature}Command.cs              # یا Query.cs
├── {Feature}CommandHandler.cs
├── {Feature}CommandValidator.cs     # FluentValidation
└── {Feature}Response.cs
```

### Pipeline MediatR (۱۱ رفتار)

ترتیب اجرا از بیرونی‌ترین به درونی‌ترین:

| # | رفتار | مسئولیت |
|---|--------|---------|
| ۱ | MetricsBehavior | هیستوگرام و شمارنده‌های System.Diagnostics.Metrics |
| ۲ | CachingBehavior | Cache-aside از طریق IDistributedCacheService |
| ۳ | RetryBehavior | Polly v8 با exponential backoff و jitter |
| ۴ | IdempotencyBehavior | ثبت اتمیک از طریق Redis |
| ۵ | PerformanceBehavior | Stopwatch با آستانه قابل پیکربندی |
| ۶ | LoggingBehavior | ILogger request lifecycle |
| ۷ | AuditBehavior | ثبت از طریق IEnumerable<IAuditService> |
| ۸ | UnitOfWorkBehavior | پیچیدن ITransactionalRequest در transaction |
| ۹ | AuthorizationBehavior | فراخوانی IAuthorizationDecisionService، fail-closed |
| ۱۰ | ValidationBehavior | FluentValidation به صورت موازی با Task.WhenAll |
| ۱۱ | TenantBehavior | اعتبارسنجی TenantId غیرخالی |

### Mediator Pipeline

```
Command/Query
    ↓
MetricsBehavior (metrika)
    ↓
CachingBehavior (cache check)
    ↓
RetryBehavior (retry)
    ↓
IdempotencyBehavior (idempotency check)
    ↓
PerformanceBehavior (timing)
    ↓
LoggingBehavior (logging)
    ↓
AuditBehavior (audit)
    ↓
UnitOfWorkBehavior (transaction)
    ↓
AuthorizationBehavior (authorization)
    ↓
ValidationBehavior (validation)
    ↓
TenantBehavior (tenant check)
    ↓
Handler (business logic)
```

---

## ۴. Event Driven Architecture

### Outbox Pattern

تمام ۵ سرویس Outbox Pattern را پیاده‌سازی می‌کنند:

- پردازش `FOR UPDATE SKIP LOCKED` برای ایمنی در concurrency
- Retry نمایی (۱۰ تلاش، ۱ ثانیه تا ۳۰ ثانیه، ضریب ۵)
- جداول dead-letter برای پیام‌های فراتر از حداکثر تلاش‌ها
- `EventEnvelope` با EventId, TenantId, CorrelationId, CausationId, EventType, Payload

### رویدادهای یکپارچه (Integration Events)

| سرویس | رویدادهای خروجی |
|--------|-----------------|
| IdentityService | UserRegistered, UserLoggedIn, UserActivated, UserLocked, UserUnlocked, UserDisabled, UserDeleted, MfaEnabled, SessionCreated, SessionRefreshTokenRotated, SessionRevoked, UserDepartmentChanged |
| AuthorizationService | RoleCreated, RoleActivated, RoleDeactivated, RoleParentSet, PermissionCreated, PermissionLifecycleChanged, RoleAssigned, RoleRevoked, PermissionGranted, PermissionRevoked, UsageRecorded, OperationResultRecorded, CacheInvalidated |
| PolicyService | PolicyCreated, PolicyUpdated, PolicyPublished, PolicyDeprecated, PolicyArchived, RuleAdded, RuleRemoved, RuleUpdated |
| TenantService | TenantCreated, TenantStatusChanged, TenantPlanUpgraded, DepartmentCreated, DepartmentStatusChanged |
| AuditService | AuditEventRecorded, SecurityEventRecorded, ComplianceEventRecorded |

### نقشه پیام‌ها (Message Queue Topology)

| صف | Consumer | Dead Letter |
|-----|----------|-------------|
| `authorization.cache` | CacheInvalidationConsumer | `.dlx` / `.dlq` |
| `audit.events` | AuditEventConsumer | `.dlx` / `.dlq` |
| `audit.security` | SecurityEventConsumer | `.dlx` / `.dlq` |
| `audit.compliance` | ComplianceEventConsumer | `.dlx` / `.dlq` |
| `tenant.cache` | TenantCacheInvalidationConsumer | `.dlx` / `.dlq` |
| `opa.sync` | OpaPolicySyncConsumer | `.dlx` / `.dlq` |

---

## ۵. مدل جداسازی Tenant (۴ لایه دفاع)

### لایه ۱: JWT Claims

توکن JWT شامل `tenant_id` و `department_id` است.

### لایه ۲: TenantMiddleware → TenantBehavior

```csharp
// TenantMiddleware.cs
// استخراج tenant_id از JWT claims
// اگر خالی باشد → Empty TenantId
```

```csharp
// TenantBehavior.cs
// اعتبارسنجی TenantId غیرخالی
// اگر خالی باشد →拒绝 درخواست
```

### لایه ۳: EF Core Global Query Filters

```csharp
// در هر DbContext
modelBuilder.Entity<T>().HasQueryFilter(e => 
    e.TenantId == _currentTenantId && !e.IsDeleted);
```

### لایه ۴: PostgreSQL RLS

```sql
-- TenantRlsInterceptor.cs
SET LOCAL app.current_tenant_id = '{tenantId}';

-- سیاست RLS در PostgreSQL
CREATE POLICY tenant_isolation ON table
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
```

### جداسازی Department

接口 `IDepartmentBound` برای فیلتر کردن در سطح Department استفاده می‌شود:
- User aggregate
- Session aggregate
- RoleAssignment aggregate
- PermissionGrant aggregate

### کلیدهای ترکیبی اولیه

تمام Aggregate از `{TenantId, Id}` به عنوان کلید ترکیبی استفاده می‌کنند — جداسازی ساختاری.

---

## ۶. مدل مجوزدهی

### جریان تصمیم‌گیری

```
سرویس → Platform.AuthorizationServiceClient
  → AuthorizationService API (/api/v1/authorization/decisions/evaluate)
    → AuthorizationDecisionService (خط لوله ۹ مرحله‌ای)
      → OpaPolicyEvaluationGateway
        → HTTP POST به OPA (/v1/data/authorization/allow)
          → OPA input را با خط‌مشی‌های Rego بارگذاری شده ارزیابی می‌کند
          → allow/deny با دلیل برمی‌گرداند
```

### خط لوله ۹ مرحله‌ای AuthorizationDecisionService

1. **بررسی وضعیت کاربر** — آیا کاربر فعال است؟
2. **بررسی وضعیت Tenant** — آیا Tenant فعال است؟
3. **بررسی Deny صریح** — آیا deny صریح وجود دارد؟
4. **بررسی محدودیت Tenant** — آیا درخواست متعلق به Tenant صحیح است؟
5. **رفع ارجاع (Delegation Resolution)** — آیا ارجاع معتبری وجود دارد؟
6. **رفع نقش (Role Resolution)** — نقش‌های مؤثر کاربر
7. **رفع مجوز (Permission Resolution)** — از طریق IEffectivePermissionResolver
8. **ارزیابی خط‌مشی (Policy Evaluation)** — از طریق OPA
9. **تصمیم نهایی** — Allow یا Deny

### الگوی Fail-Closed

هر مسیر مجوزدهی در خطا Deny برمی‌گرداند:

- `AuthorizationServiceClient` — Deny پیش‌فرض در صورت خرابی HTTP
- `FailClosedBehavior` — رفتار MediatR Forbidden برمی‌گرداند
- `OpaPolicyEvaluationGateway` — Deny در صورت خرابی/timeout OPA
- `AuthorizationDecisionService` — Deny در صورت timeout OPA

---

## ۷. سیاست‌های OPA

### `authorization.rego` (۱۴۰ خط)

خط لوله ارزیابی ۵ مرحله‌ای:

1. **دروازه Tenant** — `data.tenant.tenant_match`
2. **دروازه Department** — `resource.department_id` باید با `subject.department_id` مطابقت داشته باشد
3. **دروازه Permission** — مجوز مورد نیاز `action:resourceType` باید در `effective_permissions` باشد
4. **وضعیت Subject** — Subject باید فعال باشد
5. **Deny صریح** — اگر پرچم `explicit_deny` تنظیم شده باشد

### `tenant.rego` (۳۰ خط)

- `tenant_match` اگر `resource.ownerId == input.tenant_id`
- `tenant_active` اگر `environment.tenant_status == "active"`

---

## ۸. SharedKernel

### پریمیتیوهای Domain

| کلاس | مسئولیت |
|------|---------|
| `Entity` | پایه با Id, TenantId, CreatedAt, UpdatedAt, Version, IsDeleted |
| `AggregateRoot` | اضافه کردن Domain Events |
| `ValueObject` | equality-by-value با `GetEqualityComponents()` |
| `Result<T>` | الگوی Result برای مدیریت خطا |
| `Error` | نوع خطای یکپارچه |
| `Guard` | اعتبارسنجی ورودی |

### قراردادها

| قرارداد | مسئولیت |
|---------|---------|
| `IDomainEvent` | EventId, CorrelationId, CausationId, TenantId, OccurredAt, Version |
| `IIntegrationEvent` | ارث‌بری از IDomainEvent + EventType + Payload |
| `IAuthorizableRequest` | درخواست مجوزدهی |
| `IIdempotentRequest` | درخواست تکرارپذیر |
| `ITransactionalRequest` | درخواست تراکنشی |
| `ICachedQuery` | کوئری cache شده |
| `IRetryableRequest` | درخواست retry |

---

## ۹. الگوهای زیرساختی

### Repository Pattern

- Interface ها در Domain
- پیاده‌سازی‌ها در Infrastructure
- Cached Repositories (CachedUserRepository, CachedSessionRepository)
- AsNoTracking برای تمام queries فقط-خواندنی

### Middleware Pipeline

| # | Middleware | مسئولیت |
|---|------------|---------|
| ۱ | ExceptionHandlingMiddleware | جلوگیری از نشت جزئیات خطا به کلاینت |
| ۲ | CorrelationMiddleware | انتشار X-Correlation-Id و X-Request-Id |
| ۳ | TenantMiddleware | استخراج tenant_id از JWT |
| ۴ | RequestLoggingMiddleware | لاگ ساختاریافته با query string کامل |
| ۵ | RateLimitingMiddleware | پنجره لغزشی از طریق Redis |

### caching Infrastructure

- `RedisConnectionFactory` — thread-safe، اتصال مجدد در صورت خطا
- `RedisCacheService` — serialization JSON، degradation بدون خطا
- `DistributedIdempotencyStore` — اتمیک از طریق When.NotExists
- `RedisDistributedLockService` — اسکریپت Lua برای انتشار اتمیک
- `RedisDistributedRateLimiterService` — fail-open در صورت خطای Redis
