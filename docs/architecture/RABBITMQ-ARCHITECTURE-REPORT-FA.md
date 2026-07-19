
# گزارش معماری پیام‌رسانی RabbitMQ / MassTransit

> **گزارش صرفاً اکتشافی (Discovery-only).** هیچ کدی تغییر داده نشده است. هر گزاره به مستندات منبع با فرمت `file:line` ارجاع می‌دهد.
> راهکار (Solution): `Enterprise-Security-Platform` (شامل ۳ سرویس + SharedKernel + Platform).

---

# خلاصه مدیریتی (Executive Summary)

این پلتفرم از یک **اکسچنج موضوعی مشترک تکی (Single Shared RabbitMQ Topic Exchange)** (به نام `security.events` که از طریق `RabbitMq:ExchangeName` قابل پیکربندی است) و یک **اوتباکس تراکنشی (Transactional Outbox)** به ازای هر سرویس استفاده می‌کند. هر رویداد یکپارچه‌سازی (Integration Event) در قالب یک قرارداد جنریک واحد — `SharedKernel.Contract.Events.EventEnvelope` — سریالایز شده و از طریق `MassTransit IBus.Publish` منتشر می‌شود. از آنجا که تمام مصرف‌کنندگان (Consumers) اینترفیس `IConsumer<EventEnvelope>` را پیاده‌سازی می‌کنند، **هر صف مصرف‌کننده به همان اکسچنج مشترک با همان کلید مسیریابی (Routing Key) متصل (Bound) شده است و در نتیجه تمام پیام‌ها را دریافت می‌کند**؛ فیلتر کردن پیام‌ها در *سمت کلاینت (Client-side)* و از طریق تطابق رشته‌ای `envelope.EventType` انجام می‌شود.

- **۳ سرویس**: `IdentityService` (سرویس هویت)، `TenantService` (سرویس مستاجر)، `AuthorizationService` (سرویس مجوزدهی).
- **در مجموع ۹ صف مصرف‌کننده (Consumer Queues)** (شامل ۳ صف برای Identity، ۱ صف برای Tenant و ۵ صف برای Authorization).
- **بدون وجود Sagaها**، بدون مسیریابی تقسیم ورودی (Fan-in Routing) در `ReceiveEndpoint` و بدون اکسچنج‌های مجزا به ازای هر نوع پیام.
- **اوتباکس (Outbox)** تضمین‌کننده تحویل پیام است: رویدادهای دامنه (Domain Events) که روی اگریگیت‌ها (Aggregates) ثبت می‌شوند، در قالب یک تراکنش یکسان با عملیات نوشتن کسب‌وکار (که توسط `UnitOfWorkBehavior` مدیریت می‌شود) در جدول `OutboxMessages` دیتابیس مربوط به همان سرویس ذخیره (Flush) می‌شوند؛ سپس یک سرویس پس‌زمینه هاست‌شده (BackgroundService) به نام `OutboxProcessorBase<T>` این جدول را پولینگ (Poll) کرده و پیام‌ها را منتشر می‌سازد.

**ریسک‌های شاخص (جزئیات در بخش‌های بعدی):**
1. **مصرف‌کننده `DepartmentSoftDeleteConsumer` غیرفعال (Dead) است** — این مصرف‌کننده منتظر دریافت رویداد `DepartmentSoftDeletedV1` می‌ماند، رویدادی که **هیچ سرویسی در کل مخزن کد (Repository) آن را منتشر نمی‌کند** (سرویس TenantService بخش‌ها را با انتشار رویداد `DepartmentStatusChangedV1` حذف می‌کند). بنابراین، حذف بخش‌ها (Departments) فرآیند غیرفعال‌سازی نقش‌ها را به سرویس AuthorizationService منتقل **نمی‌کند**. *(بحرانی)*
2. **سرویس IdentityService فاقد مکانیزم بازتلاش (Retry) در سطح MassTransit یا صف نامه‌های مرده (RabbitMQ DLQ) است** (برخلاف دو سرویس دیگر) و صرفاً به بازتلاش اوتباکس دیتابیس و انتقال به جدول Dead-letter دیتابیس اتکا دارد.
3. **هر صف تمام رویدادها را دریافت می‌کند** (انتشار گسترده یا Fan-out به همه صف‌ها). این رویکرد بهینه نیست و تمام مصرف‌کنندگان را به تمام انواع رویدادها وابسته (Couple) می‌کند.
4. **چندین رویداد مرده/یتیم (Dead/Orphaned Events) و بلوک‌های Switch-Case مصرف‌کننده رها شده** وجود دارند (مانند `UserLoggedInV1`، `MfaVerifiedV1`، `authorization.role-activated.v1` و غیره).
5. **ارتباطات همزمان HTTP همچنان سرویس‌ها را در زمان درخواست به یکدیگر وابسته نگه می‌دارند** (احراز هویت لاگین، تصمیمات مجوزدهی، OPA) — پیام‌رسانی باعث کاهش وابستگی در فرآیند *انتشار اطلاعات (Propagation)* می‌شود اما *وابستگی‌های زمان اجرا (Runtime Dependencies)* را از بین نبرده است.

---

# نمای کلی پیام‌رسانی (Messaging Overview)

| حوزه تمرکز | یافته‌ها | مستندات منبع |
|---|---|---|
| بروکر پیام (Broker) | استفاده از RabbitMQ از طریق MassTransit `UsingRabbitMq` | `IdentityService.Api/Extensions/ServiceCollectionExtensions.cs:141`, `AuthorizationService.Api/Extensions/ServiceCollectionExtensions.cs:69`, `TenantService.Infrastructure/DependencyInjection.cs:107` |
| اکسچنج (Exchange) | تک اکسچنج موضوعی (Topic Exchange)، نام‌گذاری از `RabbitMq:ExchangeName` (پیش‌فرض `security.events`)، با مشخصات `Durable=true` و `ExchangeType="topic"` | فایل `RabbitMqOptions.cs` در هر سرویس؛ تنظیمات انتشار در `AuthorizationService...ServiceCollectionExtensions.cs:80-86`، `TenantService...DependencyInjection.cs:118-124`، و Identity `:151-157` |
| قرارداد پیام (Message Contract) | `EventEnvelope` (یک نوع عمومی برای تمام رویدادها) | `SharedKernel/Contract/Events/EventEnvelope.cs` |
| قرارداد مصرف‌کننده (Consumer Contract) | استفاده از `IConsumer<EventEnvelope>` به همراه ساختار کنترلی switch در سمت کلاینت بر اساس `EventType` | به عنوان نمونه: `AuthorizationCacheInvalidationConsumer.cs:12,33` |
| تضمین تحویل پیام | استفاده از Transactional Outbox مرتبط با ناشر `BackgroundService` | `Platform/Platform.Infrastructure/Outbox/OutboxProcessorBase.cs:24-97` |
| بازتلاش / صف نامه‌های مرده (Retry / DLQ) | سرویس‌های Auth و Tenant: استفاده از بازتلاش با تاخیر افزایشی نمایی `UseMessageRetry` با ۱۰ مرتبه تکرار + صف‌های حد نصاب (Quorum) + قابلیت RabbitMQ DLQ (`.dlx`/`.dlq`). سرویس Identity: **فاقد هرگونه تنظیمات در سطح گذرگاه پیام (Bus Level)** | `AuthorizationService...:88,93-122`؛ `TenantService...:126,128-133`؛ در سرویس Identity `:135-160` (فاقد retry/DLQ) |
| نسخه‌گذاری (Versioning) | تمام رویدادها به صورت هاردکد شده دارای مشخصه `Version = 1` هستند؛ رشته‌های مربوط به `EventTypeName` به شکل مکرر پسوند `V1` را در خود دارند | `IdentityDomainEvents.cs:9`, `TenantDomainEvents.cs:9`, `AuthorizationDomainEvents.cs:11` |
| ارکستراسیون (Sagas) | **هیچ موردی یافت نشد** | `جستجوی واژه Saga هیچ نتیجه‌ای نداشت` |

---

# توپولوژی رابیت‌ام‌کی (RabbitMQ Topology)

### اکسچنج (Exchange)
- **یک** اکسچنج موضوعی (Topic Exchange) با نام شناسه ثبت شده در تنظیمات به عنوان `options.ExchangeName` (پیش‌فرض `security.events`). این اکسچنج در هر سه سرویس به صورت کاملاً یکسان پیکربندی شده است:
  - سرویس Identity: فایل `ServiceCollectionExtensions.cs:151-157`
  - سرویس Authorization: فایل `ServiceCollectionExtensions.cs:80-86`
  - سرویس Tenant: فایل `DependencyInjection.cs:118-124`

### اتصال و مسیریابی (Binding / Routing - یک ویژگی معماری حیاتی)
ابزار MassTransit هر `ReceiveEndpoint` مربوط به `IConsumer<EventEnvelope>` را بر اساس **کلید مسیریابی نوع پیام (Message-Type Routing Key)** که همان `EventEnvelope` است به اکسچنج متصل می‌کند. از آنجا که قرارداد منتشر شده فارغ از ماهیت رویداد کسب‌وکار همواره از نوع `EventEnvelope` است، **هر صف با کلید مسیریابی کاملاً مشابهی به اکسچنج متصل می‌گردد و در نتیجه تمام صف‌ها کپی یکسانی از تمامی پیام‌های Envelope دریافت می‌کنند**. از مکانیزم روتینگ برای تفکیک رویدادها استفاده نمی‌شود؛ تفکیک رویدادها در داخل بدنه هر مصرف‌کننده بر اساس فیلتر رشته‌ای `EventType` اتفاق می‌افتد.

مستندات اثبات فرآیند توزیع گسترده (Fan-out): تمامی مصرف‌کنندگان از نوع `IConsumer<EventEnvelope>` هستند (مانند `OpaSyncConsumer.cs:13`، `TenantCacheInvalidationConsumer.cs:13`، `AuthorizationRoleAssignedConsumer.cs:16`)؛ نوع انتشار پیام نیز همواره `EventEnvelope` تعیین شده است (کلاس `RabbitMqMessagePublisher.cs:14` در هر سرویس). همه صف‌ها به یک اکسچنج موضوعی واحد متصل هستند.

### توپولوژی صف‌ها و نامه‌های مرده (Queues and Dead-Letter Topology)
| سرویس | نام صف | نوع حد نصاب (Quorum) | ساختار DLX / DLQ | مستندات منبع |
|---|---|---|---|---|
| Authorization | `authorization.cache` | بله | `authorization.cache.dlx` / `authorization.cache.dlq` | `ServiceCollectionExtensions.cs:93-98` |
| Authorization | `audit.pipeline` | بله | `audit.pipeline.dlx` / `audit.pipeline.dlq` | `:99-104` |
| Authorization | `opa.sync` | بله | `opa.sync.dlx` / `opa.sync.dlq` | `:105-110` |
| Authorization | `analytics.pipeline` | بله | `analytics.pipeline.dlx` / `analytics.pipeline.dlq` | `:111-116` |
| Authorization | `authorization.department-soft-delete` | بله | `authorization.department-soft-delete.dlx` / `.dlq` | `:117-122` |
| Tenant | `tenant.cache` | بله | `tenant.cache.dlx` / `tenant.cache.dlq` | `DependencyInjection.cs:128-133` |
| Identity | `authorization-role-assigned` | پیش‌فرض (کلاسیک) | **ندارد** | `ServiceCollectionExtensions.cs:137,140,158` (متد `ConfigureEndpoints` فاقد DLQ) |
| Identity | `tenant-created-cache-invalidation` | پیش‌فرض | **ندارد** | همان آدرس قبلی |
| Identity | `tenant-status-changed` | پیش‌فرض | **ندارد** | همان آدرس قبلی |

> **ناهمگونی در توپولوژی (Topology Asymmetry):** سرویس‌های Authorization و Tenant به صورت صریح از متد `ReceiveEndpoint(...)` همراه با **صف‌های حد نصاب (Quorum Queues)، صف‌های نامه‌های مرده (DLQ) در رابیت و قابلیت `UseMessageRetry`** استفاده می‌کنند. اما سرویس Identity به تنظیمات خودکار متد `ConfigureEndpoints` (با قالب نام‌گذاری کباب‌کیس) اتکا دارد و **هیچ مکانیزم بازتلاش، صف حد نصاب و صف نامه مرده رابیت‌ام‌کی** را پیکربندی نکرده است — در این سرویس صرفاً به بازتلاش اوتباکس دیتابیس (`OutboxPublishPolicy.MaxAttempts=10`) و جدول `DeadLetterMessage` در سطح دیتابیس بسنده شده است.

---

# پیکربندی MassTransit

### ساختار مشترک راه‌اندازی (در هر سه سرویس)
```csharp
AddMassTransit(cfg => {
    cfg.AddConsumer<...>();
    cfg.UsingRabbitMq((context, cfg) => {
        cfg.Host(rabbitmq://..., h => { h.Username; h.Password; h.PublisherConfirmation = true; });
        cfg.Message<EventEnvelope>(m => m.SetEntityName(options.ExchangeName));
        cfg.Publish<EventEnvelope>(p => { p.Durable = true; p.ExchangeType = "topic"; });
        // در سرویس‌های Authorization و Tenant همچنین تنظیمات زیر اعمال شده است:
        // cfg.UseMessageRetry(...); cfg.ReceiveEndpoint(...).BindDeadLetterQueue(...)
        cfg.ConfigureEndpoints(context);   // صرفاً در سرویس Identity (جهت ساخت خودکار نام صف‌ها)
    });
});
services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
services.AddHostedService<XxxOutboxProcessor>();   // یا کلاس IdentityOutboxDispatcher
````

### ناشر پیام (Publisher)

- کلاس `RabbitMqMessagePublisher : IMessagePublisher` متد `bus.Publish(envelope, ctx => { ctx.CorrelationId=...; ctx.Headers.Set("tenant_id",...); if(CausationId) ctx.Headers.Set("causation_id",...); })` را فراخوانی می‌کند — فایل‌های `IdentityService...RabbitMqMessagePublisher.cs:12-29`، `AuthorizationService...:8-29`، و `TenantService...:8-29`.
    
- متغیر `RabbitMqOptions.ExchangeName` به طور پیش‌فرض مقدار `security.events` را دارد — فایل `RabbitMqOptions.cs:23` (در هر سرویس).
    

### الگوی اوتباکس (The Outbox - مکانیزم اصلی تحویل پیام)

- کلاس پایه `OutboxProcessorBase<TDbContext> : BackgroundService` در آدرس `Platform/Platform.Infrastructure/Outbox/OutboxProcessorBase.cs` قرار دارد. این کلاس متد `ClaimPendingBatchAsync` را به طور مداوم اجرا می‌کند (تنظیمات پیش‌فرض: اندازه بچ برابر ۵۰، بازه زمانی پولینگ ۱۰ ثانیه، مدت زمان اجاره قفل یا Lease برابر ۶۰ ثانیه و سقف تکرار برابر ۱۰ — طبق کلاس `OutboxPublishPolicy.cs`). این پردازشگر ساختار `EventEnvelope` را مجدداً بازسازی (Rehydrate) کرده، متد `IMessagePublisher.PublishAsync` را فراخوانی نموده و در نهایت متد `MarkProcessedAsync` را اجرا می‌کند. در صورتی که تعداد دفعات خطا به شرط `RetryCount >= MaxAttempts` برسد، متد `MoveToDeadLetterAsync` فراخوانی می‌شود (که یک رکورد جدید در جدول `DeadLetterMessage` دیتابیس ثبت می‌کند و کاری با صف DLQ رابیت‌ام‌کی ندارد).
    
- ثبت رویدادهای دامنه در زمان فراخوانی متد `SaveChangesAsync` در DbContext هر سرویس انجام می‌شود (مانند `IdentityDbContext.cs:50-58,164-213`؛ `AuthorizationDbContext.cs:44-94`؛ `TenantDbContext.cs:46-200`).
    
- فرآیند ثبت و ذخیره نهایی رویدادها در دیتابیس توسط رفتار پایپلاین `UnitOfWorkBehavior` برای دستوراتی که اینترفیس `ITransactionalRequest` را پیاده کرده‌اند تریگر می‌شود — فایل `Platform/Platform.Behaviors/UnitOfWorkBehavior.cs:16,33` ← فراخوانی `SaveChangesAsync` ← در نتیجه رکوردهای اوتباکس به صورت کاملاً اتمیک همگام با تغییرات اگریگیت‌ها درون دیتابیس کامیت می‌شوند.
    

### حذف پیام‌های تکراری (Deduplication)

- مصرف‌کنندگان قبل از انجام هر کاری، وضعیت پیام را از طریق گارد حفاظتی `IEventConsumerDeduplicationGuard.TryBeginProcessingAsync(consumerName, EventId, 7-day TTL, ct)` (بر بستر Redis) بررسی می‌کنند تا از پردازش تکراری جلوگیری کنند — مانند فایل‌های `AuthorizationCacheInvalidationConsumer.cs:20`، `DepartmentSoftDeleteConsumer.cs:27`، `OpaSyncConsumer.cs:37`، `TenantCreatedCacheInvalidationConsumer.cs:15`. این مکانیزم مصرف‌کنندگان را در برابر سناریوهای بازتحویل پیام (At-least-once Delivery) کاملاً پیش‌بینی‌پذیر و یکنواخت (Idempotent) می‌کند.
    

# اکسچنج‌ها (Exchanges)

|**نام اکسچنج**|**نوع**|**پایداری (Durable)**|**تولیدکنندگان پیام (Producers)**|**صف‌های متصل (که تمام پیام‌ها را دریافت می‌کنند)**|
|---|---|---|---|---|
|`security.events` (پیش‌فرض)|topic|بله|هر ۳ سرویس (از طریق کلاس `RabbitMqMessagePublisher`)|`authorization.cache`, `audit.pipeline`, `opa.sync`, `analytics.pipeline`, `authorization.department-soft-delete`, `tenant.cache`, `authorization-role-assigned`, `tenant-created-cache-invalidation`, `tenant-status-changed`|

در کل سامانه **یک** اکسچنج وجود دارد. هیچ اکسچنج مجزایی به ازای رویدادها یا سناریوهای درخواست/پاسخ تعریف نشده است. تمامی انواع رویدادها بر روی همین اکسچنج تکی مالتی‌پلکس (Multiplex) می‌شوند.

# صف‌ها (Queues)

برای مشاهده هر ۹ صف، ویژگی‌های Quorum/DLQ و مستندات مربوطه، بخش "توپولوژی رابیت‌ام‌کی ← بخش صف‌ها" در بالا را ملاحظه فرمایید.

# مصرف‌کنندگان (Consumers)

## سرویس IdentityService (دارای ۳ مصرف‌کننده)

|**صف (تولید شده به صورت خودکار)**|**کلاس مصرف‌کننده**|**انواع رویدادهای تحت پوشش (EventType)**|**عوارض جانبی و عملکردها**|**مستندات منبع**|
|---|---|---|---|---|
|`authorization-role-assigned`|`AuthorizationRoleAssignedConsumer`|`authorization.role-assigned.v1`|بارگذاری کاربر در سطح مالتی‌تننت، اجرای متد `user.SynchronizeTenant(...)` ← ثبت رویداد دامنه‌ای `UserSynchronizedDomainEvent` و فراخوانی متد `SaveChangesAsync`. **تولید رویداد `UserSynchronizedV1`.**|`AuthorizationRoleAssignedConsumer.cs:21,35-61`|
|`tenant-created-cache-invalidation`|`TenantCreatedCacheInvalidationConsumer`|`TenantCreatedV1`|حذف کلید حافظه کش توزیع‌شده با ساختار `platform:shared:tenant:slug:{slug}`.|`TenantCreatedCacheInvalidationConsumer.cs:21,30-42`|
|`tenant-status-changed`|`TenantStatusChangedConsumer`|`TenantStatusChangedV1`|اگر وضعیت جدید `NewStatus` یکی از مقادیر {disabled, suspended} باشد ← ابطال تمام نشست‌های مستأجر (`session.Revoke`) ← ثبت رویداد دامنه‌ای `SessionRevokedDomainEvent` و فراخوانی متد `SaveChangesAsync`. **تولید رویداد `SessionRevokedV1`.**|`TenantStatusChangedConsumer.cs:21,35-55`|

هیچ‌کدام از مصرف‌کنندگان سرویس IdentityService درخواست HTTP همزمانی ارسال نمی‌کنند.

## سرویس TenantService (دارای ۱ مصرف‌کننده)

|**نام صف**|**کلاس مصرف‌کننده**|**انواع رویدادهای تحت پوشش (EventType)**|**عوارض جانبی و عملکردها**|**مستندات منبع**|
|---|---|---|---|---|
|`tenant.cache`|`TenantCacheInvalidationConsumer`|رویداد `TenantCreatedV1` (کش اسلاگ)؛ رویدادهای `TenantStatusChangedV1` + `TenantPlanUpgradedV1` (کش تننت)؛ رویدادهای `DepartmentCreatedV1` + `DepartmentStatusChangedV1` (کش بخش‌ها)|صرفاً حذف کلیدهای حافظه موقت (Cache). **بدون تغییر در دیتابیس، بدون انتشار رویداد خروجی و بدون فراخوانی HTTP.**|`TenantCacheInvalidationConsumer.cs:27-104`|

نکته مہم: این مصرف‌کننده صرفاً به رویدادهای خروجی **خودِ سرویس TenantService** جهت ابطال حافظه کش محلی واکنش نشان می‌دهد و پیام‌های سایر سرویس‌ها را مصرف نمی‌کند. رویدادهای `TenantNameUpdatedV1`، `DepartmentNameUpdatedV1` و `DepartmentDescriptionUpdatedV1` **فاقد بلاک Case اختصاصی** هستند ← وارد بخش `default` شده و بدون پاکسازی کش مربوطه، نادیده گرفته می‌شوند.

## سرویس AuthorizationService (دارای ۵ مصرف‌کننده)

|**نام صف**|**کلاس مصرف‌کننده**|**انواع رویدادهای تحت پوشش (EventType)**|**عوارض جانبی و عملکردها / رویداد خروجی**|**مستندات منبع**|
|---|---|---|---|---|
|`authorization.cache`|`AuthorizationCacheInvalidationConsumer`|چرخه حیات کاربر (`UserRegisteredV1`, `UserLoggedInV1`_, `UserActivatedV1`, `UserLockedV1`, `UserUnlockedV1`, `UserDisabledV1`, `UserDeletedV1`, `MfaEnabledV1`, `MfaVerifiedV1`_, `SessionRevokedV1`) + رویدادهای مجوزدهی شامل `authorization.role-assigned.v1`، `.role-revoked.v1`، `.permission-granted.v1`، `.permission-revoked.v1`|ابطال حافظه موقت مجوزها در Redis (برای یک سابجکت خاص یا کل مستأجر). بدون عملیات روی DB، بدون HTTP.|`AuthorizationCacheInvalidationConsumer.cs:33-66`؛ کلاس `Caching/CacheInvalidationConsumer.cs`|
|`audit.pipeline`|`AuditPipelineConsumer`|**تمامی پیام‌های Envelope** (فاقد گارد بررسی `EventType`)|فراخوانی `IAuthorizationAuditSink.RecordStateChangeAsync` ← اجرای کلاس `LoggingAuthorizationAuditSink` (صرفاً در حد لاگ ساده). بدون عملیات روی DB، بدون HTTP.|`AuditPipelineConsumer.cs:21,29-41`|
|`opa.sync`|`OpaSyncConsumer`|پیام‌های ساختاری شامل: `authorization.role-created/-activated/-deactivated/-disabled/-parent-changed.v1`، رویدادهای `authorization.role-assigned/-revoked.v1`، و `authorization.permission-granted/-revoked.v1`|**ارسال درخواست همزمان HTTP PUT به سرویس OPA** (فراخوانی متد `IOpaDataUpdater.SyncPolicyDataAsync`).|`OpaSyncConsumer.cs:17-52`؛ کلاس `OpaDataUpdater.cs:13-21`|
|`analytics.pipeline`|`AnalyticsPipelineConsumer`|**تمامی پیام‌های Envelope**|فراخوانی `IAnalyticsEventSink.PushEventAsync` ← اجرای کلاس `AnalyticsEventSink` (صرفاً در حد لاگ ساده).|`AnalyticsPipelineConsumer.cs:20,32-38`|
|`authorization.department-soft-delete`|`DepartmentSoftDeleteConsumer`|رویداد `DepartmentSoftDeletedV1` (**هرگز تولید نمی‌شود — مسیر مرده**)|در صورت همخوانی: غیرفعال‌سازی نقش‌های مرتبط با آن بخش ← ثبت رویداد دامنه‌ای `RoleDisabledDomainEvent` ← تولید رویداد `authorization.role-disabled.v1`.|`DepartmentSoftDeleteConsumer.cs:18,24,46-65`|

توضیح علامت `*` = رویدادهای `UserLoggedInV1` و `MfaVerifiedV1` بخش‌های بلااستفاده و مرده در ساختار شرطی (switch-cases) هستند (زیرا هرگز در سیستم تولید نمی‌شوند — رجوع شود به بخش پیام‌های مرده).

# تولیدکنندگان پیام (Publishers)

هر سرویس از طریق `RabbitMqMessagePublisher` رویدادها را در قالب گذرگاه پیام منتشر می‌کند: `bus.Publish(envelope)`. شیء Envelope بر اساس یک رویداد دامنه‌ای در متد `SaveChangesAsync` دیتابیس (بخش اوتباکس) ساخته می‌شود. تولیدکننده‌ها در واقع همان **هندلرهای دستور (Command Handlers)** هستند که اگریگیت‌ها را تغییر می‌دهند.

## تولیدکنندگان سرویس IdentityService (مسیر اوتباکس ← اکسچنج `security.events`)

|**نوع رویداد (EventType)**|**نسخه**|**عملیات تولیدکننده**|**رویداد دامنه‌ای مرتبط**|**مستندات منبع**|
|---|---|---|---|---|
|`UserRegisteredV1`|1|دستور ثبت‌نام `Register`|`UserRegisteredDomainEvent`|فایل `RegisterCommandHandler.cs:59` ← متد `User.Register` در کلاس (`User.cs:40`)|
|`SessionCreatedV1`|1|لاگین موفق `Login`|`SessionCreatedDomainEvent`|فایل `LoginCommandHandler.cs:128` ← متد `Session.Create` در کلاس (`Session.cs:37`)|
|`UserLockedV1`|1|خطا در لاگین و برقراری شرط `ShouldLock`|`UserLockedDomainEvent`|فایل `LoginCommandHandler.cs:100` ← متد `User.Lockout` در کلاس (`User.cs:252`)|
|`MfaEnabledV1`|1|فعال‌سازی ام‌اف‌ای `EnableMfa`|`MfaEnabledDomainEvent`|فایل `EnableMfaCommandHandler.cs:41` ← متد `User.EnableMfa` در کلاس (`User.cs:182`)|
|`UserActivatedV1`|1|فعال‌سازی کاربر `ActivateUser`|`UserActivatedDomainEvent`|فایل `ActivateUserCommandHandler.cs:38` ← متد `User.Activate` در کلاس (`User.cs:82`)|
|`UserUnlockedV1`|1|رفع قفل کاربر `UnlockUser`|`UserUnlockedDomainEvent`|فایل `UnlockUserCommandHandler.cs:41` ← متد `User.Unlock` در کلاس (`User.cs:107`)|
|`UserDisabledV1`|1|غیرفعال‌سازی کاربر `DisableUser`|`UserDisabledDomainEvent`|فایل `DisableUserCommandHandler.cs:38` ← متد `User.Disable` در کلاس (`User.cs:120`)|
|`UserDeletedV1`|1|حذف کاربر `DeleteUser`|`UserDeletedDomainEvent`|فایل `DeleteUserCommandHandler.cs:38` ← متد `User.Delete` در کلاس (`User.cs:134`)|
|`SessionRevokedV1`|1|خروج از سیستم؛ شناسایی سوءاستفاده از RefreshToken؛ مصرف‌کننده `TenantStatusChangedConsumer`|`SessionRevokedDomainEvent`|فایل‌های `LogoutCommandHandler.cs:38`، `RefreshTokenCommandHandler.cs:41`، و `TenantStatusChangedConsumer.cs:52`|
|`SessionRefreshTokenRotatedV1`|1|تمدید توکن `RefreshToken`|`SessionRefreshTokenRotatedDomainEvent`|فایل `RefreshTokenCommandHandler.cs:67` ← متد `Session.RotateRefreshToken` در کلاس (`Session.cs:59`)|
|`UserSynchronizedV1`|1|مصرف‌کننده `AuthorizationRoleAssignedConsumer`|`UserSynchronizedDomainEvent`|فایل `AuthorizationRoleAssignedConsumer.cs:49` ← متد `User.SynchronizeTenant` در کلاس (`User.cs:93`)|

## تولیدکنندگان سرویس TenantService

|**نوع رویداد (EventType)**|**نسخه**|**عملیات تولیدکننده**|**رویداد دامنه‌ای مرتبط**|**مستندات منبع**|
|---|---|---|---|---|
|`TenantCreatedV1`|1|دستور ساخت مستأجر `CreateTenant`|`TenantCreatedEvent`|فایل `CreateTenantCommandHandler.cs:64` ← متد `Tenant.Create` در کلاس (`Tenant.cs:53`)|
|`TenantStatusChangedV1`|1|تغییر وضعیت مستأجر `ChangeTenantStatus`|`TenantStatusChangedEvent`|فایل `ChangeTenantStatusCommandHandler.cs:26-28` ← متدهای `Tenant.Activate/Deactivate/SuspendForNonPayment`|
|`TenantPlanUpgradedV1`|1|ارتقای پلن مستأجر `UpdateTenantPlan`|`TenantPlanUpgradedEvent`|فایل `UpdateTenantPlanCommandHandler.cs:22` ← متد `Tenant.UpgradePlan` در کلاس (`Tenant.cs:140`)|
|`TenantNameUpdatedV1`|1|بروزرسانی مشخصات `UpdateTenantSettings`|`TenantNameUpdatedEvent`|فایل `UpdateTenantSettingsCommandHandler.cs:19` ← متد `Tenant.UpdateName` در کلاس (`Tenant.cs:165`)|
|`DepartmentCreatedV1`|1|ایجاد بخش جدید `CreateDepartment`|`DepartmentCreatedEvent`|فایل `CreateDepartmentCommandHandler.cs:26` ← متد `Department.Create` در کلاس (`Department.cs:42`)|
|`DepartmentStatusChangedV1`|1|حذف بخش (غیرفعال‌سازی)؛ فعال‌سازی بخش (بدون فراخوان)|`DepartmentStatusChangedEvent`|فایل `DeleteDepartmentCommandHandler.cs:25` ← متد `Department.Deactivate` در کلاس (`Department.cs:72`)|
|`DepartmentNameUpdatedV1`|1|بروزرسانی نام بخش `UpdateDepartment`|`DepartmentNameUpdatedEvent`|فایل `UpdateDepartmentCommandHandler.cs:27` ← متد `Department.UpdateName` در کلاس (`Department.cs:93`)|
|`DepartmentDescriptionUpdatedV1`|1|بروزرسانی توضیحات `UpdateDepartment`|`DepartmentDescriptionUpdatedEvent`|فایل `UpdateDepartmentCommandHandler.cs:36` ← متد `Department.UpdateDescription` در کلاس (`Department.cs:108`)|

## تولیدکنندگان سرویس AuthorizationService

|**نوع رویداد (EventType)**|**نسخه**|**عملیات تولیدکننده**|**رویداد دامنه‌ای مرتبط**|**مستندات منبع**|
|---|---|---|---|---|
|`authorization.role-created.v1`|1|ایجاد نقش `CreateRole`|`RoleCreatedDomainEvent`|فایل `CreateRoleCommandHandler.cs:34` ← متد `Role.Create` در کلاس (`Role.cs:45`)|
|`authorization.role-assigned.v1`|1|تخصیص نقش `AssignRole` / تغییر نقش کاربر `ChangeUserRole`|`RoleAssignedDomainEvent`|فایل `AssignRoleCommandHandler.cs:32` ← متد `RoleAssignment.Assign` در کلاس (`RoleAssignment.cs:33`)؛ و `ChangeUserRoleCommandHandler.cs:48-51`|
|`authorization.role-revoked.v1`|1|سلب نقش `RevokeRole` / تغییر نقش کاربر `ChangeUserRole`|`RoleRevokedDomainEvent`|فایل `RevokeRoleCommandHandler.cs:20` ← متد `RoleAssignment.Revoke` در کلاس (`RoleAssignment.cs:42`)|
|`authorization.permission-created.v1`|1|ایجاد دسترسی `CreatePermission`|`PermissionCreatedDomainEvent`|فایل‌های `CreatePermissionCommandHandler.cs:24,33` ← متد `Permission.Create` در کلاس (`Permission.cs:48`)|
|`authorization.permission-submitted-for-review.v1`|1|ثبت دسترسی جهت بررسی|`PermissionSubmittedForReviewDomainEvent`|فایل `CreatePermissionCommandHandler.cs` ← متد `Permission.SubmitForReview` در کلاس (`Permission.cs:59`)|
|`authorization.permission-approved.v1`|1|تایید دسترسی جدید|`PermissionApprovedDomainEvent`|ارجاع به متد `Permission.Approve` در کلاس (`Permission.cs:70`)|
|`authorization.permission-published.v1`|1|انتشار رسمی دسترسی|`PermissionPublishedDomainEvent`|ارجاع به متد `Permission.Publish` در کلاس (`Permission.cs:82`)|
|`authorization.permission-granted.v1`|1|اعطای دسترسی مستقیم `GrantPermission`|`PermissionGrantedDomainEvent`|فایل `GrantPermissionCommandHandler.cs:30` ← متد `PermissionGrant.Grant` در کلاس (`PermissionGrant.cs:31`)|
|`authorization.permission-revoked.v1`|1|لغو دسترسی مستقیم `RevokePermission`|`PermissionRevokedDomainEvent`|فایل `RevokePermissionCommandHandler.cs:17` ← متد `PermissionGrant.Revoke` در کلاس (`PermissionGrant.cs:40`)|
|`authorization.usage-tracking-created.v1`|1|ثبت نتیجه لاگ عملکرد (در شاخه مربوط به ردگیری جدید)|`UsageTrackingCreatedDomainEvent`|فایل‌های `RecordOperationResultCommandHandler.cs:30-41` ← متد `UsageTracking.Create` در کلاس (`UsageTracking.cs:50`)|

# رویدادهای یکپارچه‌سازی (Integration Events)

> کاتالوگ جامع تمام رویدادهای **تولیدشده**. برای هر یک: صادرکننده، مصرف‌کنندگان، اهداف کسب‌وکار و جریان‌های اجرایی شرح داده شده است.

## رویدادهای سرویس IdentityService

### رویداد `UserRegisteredV1`

- **تولیدکننده:** هندلر دستور ثبت‌نام `Register` ← فراخوانی متد `User.Register`.
    
- **مصرف‌کنندگان:** مصرف‌کننده `authorization.cache` در سرویس AuthorizationService (اجرای کلاس `AuthorizationCacheInvalidationConsumer` ← ابطال حافظه موقت سابجکت).
    
- **هدف کسب‌وکار:** ابطال کش احراز هویت بلافاصله پس از ایجاد کاربر جدید در مستأجر.
    
- **جریان اجرا:** دستور ثبت‌نام ← متد ذخیره‌سازی رفتار پایپلاین `UnitOfWorkBehavior` ← ذخیره در جدول `identity_outbox` ← پردازش توسط `IdentityOutboxDispatcher` ← انتشار در اکسچنج `security.events` ← تحویل به صف `authorization.cache` ← ابطال کش در Redis.
    
- **آیا مرده است؟** خیر. آیا ضروری است؟ بله (تضمین هماهنگی داده‌های حافظه موقت مجوزها).
    

### رویداد `SessionCreatedV1`

- **تولیدکننده:** لاگین موفق در دستور `Login` ← متد `Session.Create`.
    
- **مصرف‌کنندگان:** **در حال حاضر هیچ مصرف‌کننده‌ای درون کل مخزن پروژه وجود ندارد.**
    
- **هدف کسب‌وکار:** سیگنال ایجاد نشست کاربر (در حال حاضر فاقد مصرف‌کننده فعال است).
    
- **آیا مرده است؟** یتیم (تولید می‌شود، اما هیچ مصرف‌کننده‌ای در کل کدهای پروژه ندارد).
    

### رویداد `UserLockedV1`

- **تولیدکننده:** شکست لاگین در شرایطی که سیاست امنیتی به صورت `AuthenticationPolicy.ShouldLock` برقرار باشد.
    
- **مصرف‌کنندگان:** صف `authorization.cache` در سرویس AuthorizationService (جهت ابطال حافظه موقت کش).
    
- **هدف کسب‌وکار:** قطع دسترسی‌های کش‌شده برای کاربر قفل‌شده به دلایل امنیتی.
    
- **جریان اجرا:** خطای پیاپی ورود ← ثبت در اوتباکس ← ارسال به صف `authorization.cache` ← ابطال بلافاصله‌ی کش کاربر.
    

### رویداد `MfaEnabledV1`

- **تولیدکننده:** دستور `EnableMfa`. **مصرف‌کنندگان:** صف `authorization.cache` در سرویس AuthorizationService.
    
- **هدف کسب‌وکار:** ابطال کش در زمان تغییر یا فعال‌سازی مکانیزم احراز هویت چندعاملی (MFA).
    

### رویدادهای `UserActivatedV1` / `UserUnlockedV1` / `UserDisabledV1` / `UserDeletedV1`

- **تولیدکنندگان:** دستورات تغییر وضعیت کاربر شامل فعال‌سازی، رفع قفل، غیرفعال‌سازی یا حذف کاربر.
    
- **مصرف‌کنندگان:** صف `authorization.cache` در سرویس AuthorizationService.
    
- **هدف کسب‌وکار:** تطبیق آنی حافظه موقت مجوزها با تغییرات در چرخه حیات حساب کاربر.
    

### رویداد `SessionRevokedV1`

- **تولیدکنندگان:** دستور خروج از سیستم (Logout)، شناسایی تلاش برای تمدید توکن منقضی‌شده (Reused Refresh Token)، و در داخل مصرف‌کننده `TenantStatusChangedConsumer` (زمانی که مستأجر غیرفعال یا معلق شود ← تمامی نشست‌های فعال لغو می‌شوند).
    
- **مصرف‌کنندگان:** صف `authorization.cache` در سرویس AuthorizationService.
    
- **هدف کسب‌وکار:** پاکسازی تمام مجوزهای کش‌شده مرتبط با نشست‌های باطل‌شده.
    

### رویداد `SessionRefreshTokenRotatedV1`

- **تولیدکننده:** دستور تمدید توکن `RefreshToken` ← فراخوانی متد `Session.RotateRefreshToken`. **مصرف‌کنندگان:** فاقد مصرف‌کننده درون کدها است. به عنوان رویداد یتیم تلقی می‌شود.
    

### رویداد `UserSynchronizedV1`

- **تولیدکننده:** در مصرف‌کننده سرویس هویت با نام `AuthorizationRoleAssignedConsumer` ← فراخوانی متد `User.SynchronizeTenant` (این فرآیند با دریافت رویداد `authorization.role-assigned.v1` از سمت سرویس مجوزدهی تریگر می‌شود).
    
- **مصرف‌کنندگان:** فاقد مصرف‌کننده است. به عنوان رویداد یتیم و پایان‌دهنده زنجیره عملیات (Terminal Event) عمل می‌کند.
    

## رویدادهای سرویس TenantService

### رویداد `TenantCreatedV1`

- **تولیدکننده:** دستور ایجاد مستأجر `CreateTenant`. **مصرف‌کنندگان:** صف `tenant-created-cache-invalidation` در سرویس هویت (برای کش اسلاگ‌ها) به همراه صف اختصاصی خود سرویس مستأجر با نام `tenant.cache`.
    
- **هدف کسب‌وکار:** خالی کردن حافظه موقت اسلاگ‌های سیستم به محض ایجاد یک مستأجر جدید.
    

### رویداد `TenantStatusChangedV1`

- **تولیدکننده:** دستور تغییر وضعیت مستأجر `ChangeTenantStatus`. **مصرف‌کنندگان:** صف `tenant-status-changed` در سرویس هویت (جهت ابطال آنی نشست‌ها در صورت تعلیق/مسدودسازی) به همراه صف خود سرویس مستأجر با نام `tenant.cache`.
    

### رویداد `TenantPlanUpgradedV1`

- **تولیدکننده:** دستور ارتقای پلن `UpdateTenantPlan`. **مصرف‌کنندگان:** صرفاً صف `tenant.cache` در سرویس TenantService.
    

### رویداد `TenantNameUpdatedV1`

- **تولیدکننده:** دستور ویرایش تنظیمات مستأجر `UpdateTenantSettings`. **مصرف‌کنندگان:** **ندارد** (هیچ شرط یا کدی در کلاس `TenantCacheInvalidationConsumer` برای آن نوشته نشده است). این مورد نیز یک رویداد یتیم است ← در نتیجه، حافظه کش مربوط به آدرس `tenant-service:tenant:id:{id}` در زمان ویرایش نام مستأجر بروزرسانی و ابطال **نمی‌شود**.
    

### رویدادهای `DepartmentCreatedV1` / `DepartmentStatusChangedV1`

- **تولیدکننده:** دستورات ایجاد یا حذف بخش‌ها با نام‌های `CreateDepartment` / `DeleteDepartment`. **مصرف‌کنندگان:** صف `tenant.cache` در سرویس مستأجر (جهت بروزرسانی حافظه موقت مربوط به ساختار بخش‌ها).
    

### رویدادهای `DepartmentNameUpdatedV1` / `DepartmentDescriptionUpdatedV1`

- **تولیدکننده:** دستور ویرایش بخش `UpdateDepartment`. **مصرف‌کنندگان:** **ندارد** (وارد بخش پردازش پیش‌فرض `default` در ساختار کد شده و نادیده گرفته می‌شوند). به عنوان پیام‌های یتیم طبقه‌بندی می‌شوند.
    

## رویدادهای سرویس AuthorizationService

### رویداد `authorization.role-created.v1`

- **تولیدکننده:** دستور ایجاد نقش `CreateRole`. **مصرف‌کنندگان:** صف داخلی `opa.sync` (برای اعمال در مستندات نقش‌های سرویس OPA). این رویداد توسط سایر سرویس‌ها دریافت نمی‌شود.
    

### رویداد `authorization.role-assigned.v1`

- **تولیدکننده:** دستور انتساب نقش `AssignRole` یا تغییر نقش کاربر `ChangeUserRole`. **مصرف‌کنندگان:** **بین‌سرویسی**: صف `authorization-role-assigned` در سرویس هویت (جهت همگام‌سازی کاربر ← تولید رویداد پایانی `UserSynchronizedV1`)؛ به‌علاوه صف‌های داخلی شامل `opa.sync` (تخصیص‌ها)، صف `authorization.cache` (ابطال کش سابجکت مربوطه)، و صف `audit.pipeline`.
    

### رویداد `authorization.role-revoked.v1`

- **تولیدکننده:** دستور لغو نقش `RevokeRole` یا تغییر نقش کاربر `ChangeUserRole`. **مصرف‌کنندگان:** صف‌های داخلی شامل `opa.sync`، صف `authorization.cache` و صف `audit.pipeline`. هیچ سرویس دیگری این پیام را مصرف نمی‌کند.
    

### رویدادهای چهارگانه چرخه حیات دسترسی (`authorization.permission-created/submitted-for-review/approved/published.v1`)

- **تولیدکننده:** فرآیند ایجاد یک دسترسی جدید در دستور `CreatePermission` (یک تابع به تنهایی هر ۴ رویداد را در مراحل مختلف ثبت می‌کند). **مصرف‌کنندگان:** صرفاً صف داخلی `audit.pipeline` جهت ثبت لاگ.
    

### رویدادهای `authorization.permission-granted.v1` / `.permission-revoked.v1`

- **تولیدکننده:** دستورات اعطا یا لغو مستقیم دسترسی کاربر `GrantPermission` / `RevokePermission`. **مصرف‌کنندگان:** صف‌های داخلی شامل `opa.sync` (ثبت در مجوزها)، صف `authorization.cache` (ابطال دسترسی کل کاربران ذیل آن نقش در کش Redis)، و صف `audit.pipeline`.
    

### رویداد `authorization.usage-tracking-created.v1`

- **تولیدکننده:** ثبت عملکرد در دستور `RecordOperationResult` (در مسیر کدهای جدید مربوط به مانیتورینگ). **مصرف‌کنندگان:** صف داخلی `audit.pipeline`.
    

# نمودار وابستگی رویدادها (Event Dependency Graph)

```
[IdentityService] (سرویس هویت)
Register ─► UserRegisteredV1 ─────────────► AuthorizationService(authorization.cache: ابطال کش سابجکت)
Login(موفق) ─► SessionCreatedV1 ─────────► (فاقد مصرف‌کننده)
Login(ناموفق+قفل) ─► UserLockedV1 ──────► AuthorizationService(authorization.cache)
Login ─► [فراخوانی همزمان HTTP] TenantServiceClient.ResolveTenantIdBySlugAsync
Login ─► [فراخوانی همزمان HTTP] AuthorizationRoleResolver.GetActiveRoleForUserAsync
EnableMfa ─► MfaEnabledV1 ───────────────► AuthorizationService(authorization.cache)
ActivateUser/UnlockUser/DisableUser/DeleteUser ─► User*V1 ─► AuthorizationService(authorization.cache)
Logout/RefreshToken(سوءاستفاده)/TenantStatusChangedConsumer ─► SessionRevokedV1 ─► AuthorizationService(authorization.cache)
TenantStatusChangedConsumer ─(ابطال تمام نشست‌ها)─► SessionRevokedV1 ─► AuthorizationService(authorization.cache)
AuthorizationRoleAssignedConsumer ─(همگام‌سازی کاربر)─► UserSynchronizedV1 ─► (فاقد مصرف‌کننده)

[TenantService] (سرویس مستأجر)
CreateTenant ─► TenantCreatedV1 ──────────► IdentityService(tenant-created-cache-invalidation) + خود سرویس(tenant.cache)
ChangeTenantStatus ─► TenantStatusChangedV1 ─► IdentityService(tenant-status-changed: ابطال نشست‌ها) + خود سرویس(tenant.cache)
UpdateTenantPlan ─► TenantPlanUpgradedV1 ─► خود سرویس(tenant.cache)
UpdateTenantSettings ─► TenantNameUpdatedV1 ─► (وارد حالت دیفالت شده و نادیده گرفته می‌شود)
CreateDepartment ─► DepartmentCreatedV1 ──► خود سرویس(tenant.cache)
DeleteDepartment ─► DepartmentStatusChangedV1 ─► خود سرویس(tenant.cache)   ⚠ به جای رویداد فرضی DepartmentSoftDeletedV1
UpdateDepartment ─► Department*UpdatedV1 ─► (وارد حالت دیفالت شده و نادیده گرفته می‌شود)
[دستورات تغییر داده در سرویس مستأجر] ─► [فراخوانی همزمان HTTP] AuthorizationServiceClient (پایپلاین رفتار AuthorizationBehavior)

[AuthorizationService] (سرویس مجوزدهی)
CreateRole ─► authorization.role-created.v1 ─► خود سرویس(opa.sync ──► درخواست تحت شبکه HTTP به سرویس OPA)
AssignRole/ChangeUserRole ─► authorization.role-assigned.v1 ─► IdentityService(authorization-role-assigned ──► تولید UserSynchronizedV1) + خود سرویس(opa.sync, authorization.cache, audit.pipeline)
RevokeRole ─► authorization.role-revoked.v1 ─► خود سرویس(opa.sync, authorization.cache, audit.pipeline)
CreatePermission ─► انتشار ۴ رویداد چرخه حیات دسترسی ─► خود سرویس(audit.pipeline)
GrantPermission ─► authorization.permission-granted.v1 ─► خود سرویس(opa.sync, authorization.cache, audit.pipeline)
RevokePermission ─► authorization.permission-revoked.v1 ─► خود سرویس(opa.sync, authorization.cache, audit.pipeline)
RecordOperationResult ─► authorization.usage-tracking-created.v1 ─► خود سرویس(audit.pipeline)
کلاس DepartmentSoftDeleteConsumer در انتظار رویداد DepartmentSoftDeletedV1 ─► (هرگز تولید نمی‌شود ──► مسیر کاملاً مرده)
   └─ در صورت همخوانی احتمالی در آینده: غیرفعال‌سازی نقش‌ها ──► رویداد authorization.role-disabled.v1 ──► خود سرویس(opa.sync, audit.pipeline)
CreateRole ─► [فراخوانی همزمان HTTP] TenantServiceClient (بررسی وجود یا عدم وجود بخش یا دپارتمان مربوطه)
EvaluateAuthorizationDecision ─► [فراخوانی همزمان HTTP] به سرویس OPA
OpaSyncConsumer ─► [فراخوانی همزمان HTTP] به سرویس OPA
```

**ارتباطات و اتصالات فرامرز سرویس‌ها:** مسیرهای خروجی از TenantService به IdentityService (شامل ۲ لبه ارتباطی)، مسیر خروجی از AuthorizationService به IdentityService (شامل ۱ لبه ارتباطی)، تعامل همزمان HTTP از سرویس IdentityService به AuthorizationService برای اعتبارسنجی فرآیند مجوزدهی در زمان اجرا، تعامل همزمان HTTP از سرویس TenantService به AuthorizationService از طریق زنجیره Pipeline سراسری سیستم، تعامل همزمان HTTP از سرویس AuthorizationService به TenantService در زمان اجرای هندلر ایجاد نقش، و در نهایت تعامل همزمان HTTP از سرویس AuthorizationService به سیستم بیرونی OPA.

# ماتریس انتشار → مصرف (Publish → Consume Matrix)

|**نام رویداد**|**ناشر پیام (Publisher)**|**مصرف‌کنندگان درون پروژه**|**هدف از ارسال پیام**|
|---|---|---|---|
|`UserRegisteredV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`SessionCreatedV1`|سرویس Identity|—|(رویداد یتیم)|
|`UserLockedV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`MfaEnabledV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`UserActivatedV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`UserUnlockedV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`UserDisabledV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`UserDeletedV1`|سرویس Identity|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`SessionRevokedV1`|سرویس Identity (+ مصرف‌کننده داخلی)|صف Authorization(`authorization.cache`)|ابطال کش مجوزها|
|`SessionRefreshTokenRotatedV1`|سرویس Identity|—|(رویداد یتیم)|
|`UserSynchronizedV1`|سرویس Identity (از طریق مصرف‌کننده)|—|(رویداد یتیم و پایانی زنجیره)|
|`TenantCreatedV1`|سرویس Tenant|صف Identity(`tenant-created-cache-invalidation`) و صف Tenant(`tenant.cache`)|بازسازی کش اسلاگ‌ها و تننت|
|`TenantStatusChangedV1`|سرویس Tenant|صف Identity(`tenant-status-changed`) و صف Tenant(`tenant.cache`)|لغو نشست‌های فعال و ابطال کش|
|`TenantPlanUpgradedV1`|سرویس Tenant|صف Tenant(`tenant.cache`)|ابطال کش مستأجر|
|`TenantNameUpdatedV1`|سرویس Tenant|—|(رویداد یتیم و نادیده گرفته شده)|
|`DepartmentCreatedV1`|سرویس Tenant|صف Tenant(`tenant.cache`)|ابطال کش دپارتمان‌ها|
|`DepartmentStatusChangedV1`|سرویس Tenant|صف Tenant(`tenant.cache`)|ابطال کش دپارتمان‌ها|
|`DepartmentNameUpdatedV1`|سرویس Tenant|—|(رویداد یتیم و نادیده گرفته شده)|
|`DepartmentDescriptionUpdatedV1`|سرویس Tenant|—|(رویداد یتیم و نادیده گرفته شده)|
|`authorization.role-created.v1`|سرویس Authorization|صف Authorization(`opa.sync`)|همگام‌سازی سیاست‌های سرویس OPA|
|`authorization.role-assigned.v1`|سرویس Authorization|صف Identity(`authorization-role-assigned`) و صف‌های داخلی Authorization(`opa.sync`، `authorization.cache`، `audit.pipeline`)|فرآیند همگام‌سازی کاربر + OPA + ابطال کش + مانیتورینگ|
|`authorization.role-revoked.v1`|سرویس Authorization|صف‌های داخلی Authorization(`opa.sync`، `authorization.cache`، `audit.pipeline`)|همگام‌سازی OPA + ابطال کش + مانیتورینگ|
|`authorization.permission-created.v1`|سرویس Authorization|صف Authorization(`audit.pipeline`)|سیستم ثبت وقایع و لاگین|
|`authorization.permission-submitted-for-review.v1`|سرویس Authorization|صف Authorization(`audit.pipeline`)|سیستم ثبت وقایع و لاگین|
|`authorization.permission-approved.v1`|سرویس Authorization|صف Authorization(`audit.pipeline`)|سیستم ثبت وقایع و لاگین|
|`authorization.permission-published.v1`|سرویس Authorization|صف Authorization(`audit.pipeline`)|سیستم ثبت وقایع و لاگین|
|`authorization.permission-granted.v1`|سرویس Authorization|صف‌های داخلی Authorization(`opa.sync`، `authorization.cache`، `audit.pipeline`)|همگام‌سازی OPA + ابطال کش + مانیتورینگ|
|`authorization.permission-revoked.v1`|سرویس Authorization|صف‌های داخلی Authorization(`opa.sync`، `authorization.cache`، `audit.pipeline`)|همگام‌سازی OPA + ابطال کش + مانیتورینگ|
|`authorization.usage-tracking-created.v1`|سرویس Authorization|صف Authorization(`audit.pipeline`)|سیستم ثبت وقایع و لاگین|

**رویدادهای مصرف‌شده ولی هرگز منتشرنشده (مصرف‌کنندگان مرده یا کدهای شرطی فاقد کاربرد):**

- رویداد `DepartmentSoftDeletedV1` (که انتظار می‌رود توسط `DepartmentSoftDeleteConsumer` مصرف شود) — **هیچ کدی برای انتشار آن در هیچ بخشی از برنامه‌ها وجود ندارد**.
    
- رویداد `UserLoggedInV1` (تعریف شده در کلاس شرطی `AuthorizationCacheInvalidationConsumer.cs:36`) — با وجود معرفی ساختار شیء رویداد با نام `UserLoggedInDomainEvent` در کدهای دامنه‌ای، اما در فرآیند واقعی هیچ کدی آن را نمونه‌سازی نمی‌کند؛ چرا که فرآیند ورود در عمل پیام `SessionCreatedV1` را صادر می‌سازد. یک شاخه مرده و بلااستفاده در کد است.
    
- رویداد `MfaVerifiedV1` (تعریف شده در کدهای شرطی `AuthorizationCacheInvalidationConsumer.cs:43`) — فاقد کلاس رویداد دامنه‌ای متناظر و یا کدهای انتشار است (در بدنه دستور `MfaVerifyCommandHandler` هیچ تغییری ثبت و رویدادی پرتاب نمی‌شود). یک شاخه کاملاً مرده است.
    

# نمودارهای توالی پیام‌ها (Sequence Diagrams - Mermaid)

### فرآیند ایجاد مستأجر (Tenant Created)

Code snippet

```
sequenceDiagram
    participant C as Client
    participant TS as TenantService
    participant OB as tenant_outbox
    participant MQ as security.events
    participant I as IdentityService(tenant-created-cache-invalidation)
    participant TC as TenantService(tenant.cache)
    C->>TS: CreateTenant (ایجاد مستأجر)
    TS->>OB: SaveChanges (ثبت رویداد TenantCreatedV1)
    TS-->>C: کد پاسخ ۲۰۰ موفق
    OB->>MQ: انتشار پیام TenantCreatedV1
    MQ->>I: تحویل پیام
    I->>I: حذف کش با ساختار platform:shared:tenant:slug:{slug}
    MQ->>TC: تحویل پیام
    TC->>TC: پاکسازی کش‌های محلی اسلاگ و تننت
```

### فرآیند تخصیص نقش (زنجیره ارتباطی فرامرزی سرویس‌ها)

Code snippet

```
sequenceDiagram
    participant C as Client
    participant AZ as AuthorizationService
    participant OB as authz_outbox
    participant MQ as security.events
    participant I as IdentityService(authorization-role-assigned)
    participant IOB as identity_outbox
    participant OP as opa.sync
    participant AC as authorization.cache
    participant AU as audit.pipeline
    C->>AZ: AssignRole (تخصیص نقش)
    AZ->>OB: SaveChanges (ثبت رویداد role-assigned.v1)
    AZ-->>C: کد پاسخ ۲۰۰ موفق
    OB->>MQ: انتشار پیام به اکسچنج موضوعی
    MQ->>I: تحویل به صف هویت
    I->>IOB: SaveChanges (ثبت رویداد فرعی UserSynchronizedV1)
    MQ->>OP: تحویل پیام ← ارسال درخواست شبکه HTTP PUT به OPA
    MQ->>AC: تحویل پیام ← ابطال کش کاربر در Redis
    MQ->>AU: تحویل پیام ← ثبت در فایل لاگ مانیتورینگ
    IOB->>MQ: انتشار رویداد نهایی UserSynchronizedV1 (بدون مصرف‌کننده)
```

### غیرفعال‌سازی مستأجر (زنجیره ابطال نشست‌ها)

Code snippet

```
sequenceDiagram
    participant C as Client
    participant TS as TenantService
    participant TOB as tenant_outbox
    participant MQ as security.events
    participant I as IdentityService(tenant-status-changed)
    participant IOB as identity_outbox
    participant AC as AuthorizationService(authorization.cache)
    C->>TS: ChangeTenantStatus (تغییر وضعیت به غیرفعال)
    TS->>TOB: SaveChanges (ثبت رویداد TenantStatusChangedV1)
    TOB->>MQ: انتشار پیام در رابیت
    MQ->>I: تحویل پیام به صف هویت
    I->>IOB: لغو تمامی نشست‌های فعال ← ثبت رویداد SessionRevokedV1
    MQ->>AC: دریافت SessionRevokedV1 ← ابطال حافظه کش کاربران در Redis
    IOB->>MQ: انتشار نهایی پیام SessionRevokedV1 (توسط صف AC مصرف می‌شود)
```

### لاگین و ورود کاربر (ترکیب تعاملات همزمان و ناهمزمان)

Code snippet

```
sequenceDiagram
    participant C as Client
    participant I as IdentityService(Login)
    participant T as TenantService (درخواست همزمان HTTP)
    participant R as AuthorizationService (درخواست همزمان HTTP, نقش کاربر)
    participant IOB as identity_outbox
    participant MQ as security.events
    participant AC as AuthorizationService(authorization.cache)
    C->>I: Login(identifier) (ارسال اطلاعات لاگین)
    I->>T: فراخوانی متد ResolveTenantIdBySlugAsync (تعامل همزمان)
    I->>R: فراخوانی متد GetActiveRoleForUserAsync (تعامل همزمان)
    I->>IOB: SaveChanges (ثبت رویداد SessionCreatedV1 یا UserLockedV1)
    IOB->>MQ: انتشار پیام در صف
    MQ->>AC: تحویل پیام (تنها در شرایط ثبت رویداد قفل شدن حساب کاربر)
```

### حذف بخش (مکانیزم معیوب و شکسته در زنجیره انتشار)

Code snippet

```
sequenceDiagram
    participant C as Client
    participant TS as TenantService
    participant TOB as tenant_outbox
    participant MQ as security.events
    participant D as AuthorizationService(authorization.department-soft-delete)
    C->>TS: DeleteDepartment (درخواست حذف بخش)
    TS->>TOB: SaveChanges (ثبت رویداد DepartmentStatusChangedV1)
    TOB->>MQ: انتشار رویداد DepartmentStatusChangedV1
    MQ->>D: تحویل پیام DepartmentStatusChangedV1 به صف هدف
    Note over D: مصرف‌کننده داخلی تنها با رویداد DepartmentSoftDeletedV1 کار می‌کند ← متوقف شده و هیچ کاری انجام نمی‌دهد!
    Note over D: نقش‌های مرتبط با بخش‌های حذف شده هرگز در دیتابیس مجوزدهی غیرفعال نمی‌شوند!
```

# رویدادها و پیام‌های مرده (Dead Messaging)

## پیام‌های منتشرشده فاقد مصرف‌کننده (Orphaned Events)

- رویداد `SessionCreatedV1` (سرویس Identity) — فاقد هرگونه مصرف‌کننده در کل پروژه.
    
- رویداد `SessionRefreshTokenRotatedV1` (سرویس Identity) — فاقد هرگونه مصرف‌کننده در کل پروژه.
    
- رویداد `UserSynchronizedV1` (سرویس Identity، صادر شده از خروجی مصرف‌کننده) — فاقد مصرف‌کننده است.
    
- رویدادهای `TenantNameUpdatedV1`، `DepartmentNameUpdatedV1` و `DepartmentDescriptionUpdatedV1` (سرویس Tenant) — تولید و ذخیره می‌شوند اما کلاس مصرف‌کننده `TenantCacheInvalidationConsumer` فاقد بلاک شرطی بررسی این گزینه‌ها است؛ این موارد نادیده گرفته شده و در نتیجه کش‌های مرتبط با آن‌ها هرگز ابطال **نمی‌شوند**. مستندات اثبات: ساختار کلاس `TenantCacheInvalidationConsumer.cs:27-45`.
    

## رویدادهای تعریف‌شده فاقد کد تولیدکننده (Dead Consumers / Dead Switch-Cases)

- **رویداد `DepartmentSoftDeletedV1`** — مصرف‌کننده مربوطه در آدرس `DepartmentSoftDeleteConsumer.cs:18,24` در انتظار دریافت این پیام است، اما جستجوی واژه کلیدی نشان می‌دهد **هیچ تولیدکننده‌ای** برای آن در هیچ بخشی از کدهای پروژه وجود ندارد. سیستم حذف بخش‌ها در کلاس `DeleteDepartmentCommandHandler.cs:25` پیام `DepartmentStatusChangedV1` را صادر می‌سازد. ← این مصرف‌کننده هرگز اجرا نخواهد شد. **این بزرگترین نقص در کدهای فعلی است: حذف یک بخش در سیستم مستأجر عملاً باعث غیرفعال‌سازی نقش‌های زیرمجموعه آن بخش در سرویس مجوزدهی نمی‌شود.** _(سطح اهمیت: بحرانی)_
    
- **رویداد `UserLoggedInV1`** — به عنوان یک بخش شرطی در فایل `AuthorizationCacheInvalidationConsumer.cs:36` نوشته شده است؛ اگرچه شیء رویداد دامنه‌ای `UserLoggedInDomainEvent` معرفی شده (`IdentityDomainEvents.cs:25-28`) اما هیچ بخشی از برنامه آن را نمونه‌سازی نکرده است (عملیات ورود با موفقیت پیام `SessionCreatedV1` را ثبت می‌کند). این بخش از کد بلااستفاده و کاملاً مرده است.
    
- **رویداد `MfaVerifiedV1`** — به عنوان کدهای شرطی در فایل `AuthorizationCacheInvalidationConsumer.cs:43` نوشته شده است؛ اما هیچ ساختار رویداد دامنه‌ای معادل یا کلاس انتشاری برای آن وجود ندارد (در بدنه دستور `MfaVerifyCommandHandler` هیچ فرآیند انتشاری پیاده‌سازی نشده است). یک شاخه غیرکاربردی و کاملاً مرده است.
    

## رویدادهای دامنه‌ای معرفی‌شده فاقد کدهای تولید (Dead Contracts)

- **سرویس IdentityService:** رویدادهای دامنه‌ای معرفی‌شده شامل `UserLoggedInDomainEvent`، `MfaDisabledDomainEvent` (متد `User.DisableMfa` هرگز فراخوانی نمی‌شود)، `PasswordChangedDomainEvent` (متد `User.ChangePassword` بدون فراخوان است) و `PasswordResetDomainEvent` (متد `User.ResetPassword` فاقد صدازننده است).
    
- **سرویس AuthorizationService:** رویدادهای `authorization.role-activated.v1`، `.role-deactivated.v1`، `.role-parent-changed.v1` (توابع اگریگیت مانند `Role.Activate/Deactivate/SetParent` فاقد صدازننده زمان اجرا هستند)، رویدادهای `.permission-deprecated.v1`، `.permission-archived.v1`، `.permission-version-created.v1` (توابع متناظر روی مدل Permission بدون صدازننده هستند)، رویداد `.evaluated.v1` (رویداد دامنه‌ای `AuthorizationEvaluatedDomainEvent` هیچ نمونه‌سازی جدیدی در کل بدنه کدها ندارد) و رویداد `.usage-incremented.v1` (که تنها در داخل سرویس `UsageAccountingService.RecordUsageAsync` تعریف شده است، سرویسی که در آدرس `DependencyInjection.cs:107` رجیستر شده اما **هیچ کلاس و هندلری در کل برنامه از آن استفاده نمی‌کند**).
    

## مصرف‌کنندگان یا تولیدکنندگان هم‌پوشان و تکراری

- **فاقد مصرف‌کننده تکراری** (هر صف منحصراً توسط یک کلاس مصرف‌کننده مجزا پردازش می‌شود).
    
- **فاقد تولیدکننده هم‌پوشان برای رویداد مشترک** (هیچ رویدادی با نام یکسان توسط دو سرویس متفاوت صادر نمی‌شود).
    
- درون سرویس AuthorizationService، چند هندلر دستور رویداد مشابهی را ارسال می‌کنند که این کار بر اساس منطق طراحی سیستم کاملاً طبیعی است (دستورات `AssignRole` و `ChangeUserRole` هر دو رویداد `authorization.role-assigned.v1` را تولید می‌کنند؛ متدهای `RevokeRole` و `ChangeUserRole` نیز رویداد `authorization.role-revoked.v1` را صادر می‌کنند) — این رفتار ویژگی ساختاری پروژه است و نقص فنی تلقی نمی‌شود.
    

## رجیسترها، صف‌ها یا اکسچنج‌های مرده و معلق

- صف با نام `authorization.department-soft-delete` عملاً در وضعیت بلااستفاده و کاملاً مرده قرار دارد (زیرا مصرف‌کننده متصل به آن با رویداد دریافتی هیچ همخوانی ندارد).
    
- فاقد اکسچنج مرده یا بلااستفاده (یک اکسچنج واحد معرفی شده که در فرآیندهای سیستم نقش ایفا می‌کند).
    
- سرویس هویت ۳ مصرف‌کننده مجزا را رجیستر می‌کند اما **هیچ صف نامه‌های مرده (DLQ) یا لایه بازتلاشی** برای آن معرفی نشده است — این موضوع به معنای غیرفعال بودن این بخش نیست، اما نشان‌دهنده ضعف جدی در دسترسی‌پذیری و پایداری پیام‌ها در مقایسه با سایر سرویس‌ها است.
    

# پیام‌ها و فرآیندهای تکراری (Duplicate Messaging)

- **عملیات ابطال کش برای تغییرات نقش‌ها و دسترسی‌ها دو بار انجام می‌شود:** هندلرهای دستور پس از اعمال تغییرات، به طور مستقیم و همزمان اقدام به پاکسازی از طریق رابط `IAuthorizationCache.InvalidateSubjectAsync` می‌کنند (مانند هندلر `AssignRoleCommandHandler.cs:35`)، و از طرف دیگر همان پیام پس از انتشار مجدداً توسط مصرف‌کننده `AuthorizationCacheInvalidationConsumer` کش دیتابیس را باطل می‌کند. این مکانیزم برای تغییرات ثبت‌شده در درون یک سرویس تا حدی **تکراری و اضافی** است، اما تضمین‌کننده همگام‌سازی فرامرزی با رویدادهای صادره از سرویس IdentityService است. مستندات اثبات: هندلرها + کدهای کلاس `AuthorizationCacheInvalidationConsumer.cs:48-62`.
    
- هر ۹ صف فعال در سیستم، کپی‌های مشابهی از تمام پیام‌های صادر شده را دریافت می‌کنند (الگوی انتشار سراسری یا Fan-out) ← در نتیجه تمام مصرف‌کنندگان پیام‌ها را از حالت فشرده خارج کرده (Deserialize)، هدرها را خوانده و نوع آن را بررسی می‌کنند تا در صورت عدم تطابق آن را دور بریزند. این موضوع به معنای تکرار در لایه کسب‌وکار نیست، اما جابجایی دیتای بیهوده و تکراری در لایه انتقال شبکه به حساب می‌آید.
    

# وابستگی‌های حلقوی (Circular Dependencies)

## حلقه‌های پیام‌رسانی (وابستگی متقابل سرویس‌ها به یکدیگر از طریق رویدادها)

هیچ حلقه تکرار شونده بی‌نهایتی در پیام‌های رد و بدل شده یافت نشد. زنجیره رویدادهای تولیدشده در فرآیندهای ثانویه همگی در یک نقطه مشخص متوقف و نهایی می‌شوند:

- فرآیند `authorization.role-assigned.v1` ← سرویس هویت ← ثبت پیام `UserSynchronizedV1` (پایان‌دهنده زنجیره، فاقد مصرف‌کننده).
    
- فرآیند `TenantStatusChangedV1` ← سرویس هویت ← ثبت پیام `SessionRevokedV1` ← ابطال کش در سرویس مجوزدهی (پایان‌دهنده زنجیره).
    
- فرآیند فرضی `DepartmentSoftDeletedV1` (در صورت اصلاح و کارکرد درست در آینده) ← ثبت پیام `authorization.role-disabled.v1` ← ارسال به صف‌های `opa.sync` و `audit.pipeline` (پایان‌دهنده زنجیره).
    
    هیچ‌کدام از این موارد به نقطه شروع بازنمی‌گردند تا سیستم را دچار رفتارهای تکرارشونده ناخواسته کنند. **هیچ وابستگی حلقوی ارادی یا غیرارادی در لایه رویدادها در این پلتفرم وجود ندارد.**
    

## وابستگی حلقوی در زمان اجرا در سطح سرویس‌ها (Runtime Circular Dependency)

دقیقاً یک وابستگی حلقوی دوطرفه در زمان اجرا بین دو سرویس IdentityService و AuthorizationService وجود دارد:

- تعامل AuthorizationService ← (از طریق رویداد) ← سرویس IdentityService: رویداد `authorization.role-assigned.v1` توسط مصرف‌کننده `AuthorizationRoleAssignedConsumer` در سرویس هویت دریافت می‌شود.
    
- تعامل IdentityService ← (فراخوانی همزمان HTTP) ← سرویس AuthorizationService: رابط تعاملی `IAuthorizationDecisionService` (پیاده‌سازی شده در کلاس کلاینت `AuthorizationServiceClient`) در کدهای مربوط به هندلرهای `Logout/EnableMfa/ActivateUser/DisableUser/DeleteUser/UnlockUser` فراخوانی می‌شود و از طرف دیگر رابط `IAuthorizationRoleResolver` در زمان فرآیند ورود لاگین `Login` بکار گرفته می‌شود. مستندات ثبت این کلاینت‌ها در فایل کدهای `IdentityService...ServiceCollectionExtensions.cs:175,221`.
    
- تعامل TenantService ← (فراخوانی همزمان HTTP) ← سرویس AuthorizationService از طریق زنجیره کدهای خط لوله سراسری `AuthorizationBehavior` (در فایل `Platform/Platform.Behaviors/AuthorizationBehavior.cs:39`) به ازای تمامی دستورات دارای اینترفیس `IAuthorizableRequest`.
    
- تعامل AuthorizationService ← (فراخوانی همزمان HTTP) ← سرویس TenantService در هندلر دستور `CreateRoleCommandHandler` (جهت بررسی وضعیت وجود یا عدم وجود بخش مربوطه).
    

این موارد حلقه‌های انتقال پیام نیستند، اما نشان می‌دهند سرویس‌ها در زمان اجرا قابلیت استقرار کاملاً مستقل (Independent Deployability) ندارند؛ قطع سرویس مجوزدهی در جا فرآیند ورود کاربران در سرویس هویت و ثبت دستورات در سرویس مستأجر را با خطا مواجه و متوقف می‌کند.

# مقایسه تعاملات همزمان و ناهمزمان (Synchronous vs Asynchronous Calls)

پیام‌رسانی وظیفه غیرهمگام‌سازی و مستقل کردن **فرآیند توزیع رویدادها** را به دوش می‌کشد، اما کدهای سیستم در مسیر تراکنش‌های اصلی همچنان به **فراخوانی‌های همزمان HTTP** وابسته هستند که عملاً سرویس‌ها را در زمان خواندن و نوشتن به یکدیگر گره می‌زند:

|**کلاس فراخوان دهنده**|**سرویس هدف**|**نوع تعامل**|**مستندات منبع**|
|---|---|---|---|
|فایل `LoginCommandHandler.cs:51`|سرویس TenantService (یافتن شناسه با اسلاگ)|همزمان HTTP|متد کلاینت `TenantServiceClient.ResolveTenantIdBySlugAsync`|
|فایل `LoginCommandHandler.cs:139`|سرویس AuthorizationService (یافتن نقش کاربر)|همزمان HTTP|متد کلاینت `AuthorizationRoleResolver.GetActiveRoleForUserAsync`|
|هندلرهای دستورات `Logout/EnableMfa/ActivateUser/DisableUser/DeleteUser/UnlockUser`|سرویس AuthorizationService (بررسی دسترسی و تصمیم‌گیری)|همزمان HTTP|متد کلاینت ثبت شده `IAuthorizationDecisionService` در آدرس `IdentityService...:175`|
|فایل `AuthorizationBehavior` (پایپلاین سراسری دستورات)|سرویس AuthorizationService (اعتبارسنجی تصمیم نهایی)|همزمان HTTP|فایل `Platform/Platform.Behaviors/AuthorizationBehavior.cs:39` (فیلتر شده بر اساس شروط خاص)|
|هندلر دستور `CreateRoleCommandHandler`|سرویس TenantService (بررسی صحت شناسه دپارتمان)|همزمان HTTP|متد کلاینت `TenantServiceClient` ثبت شده در آدرس `DependencyInjection.cs:122`|
|هندلر دستور `EvaluateAuthorizationDecisionCommandHandler`|سرویس بیرونی OPA|همزمان HTTP|فراخوانی متد تحت شبکه `OpaHttpClient.PostAsJsonAsync`|
|مصرف‌کننده `OpaSyncConsumer`|سرویس بیرونی OPA|همزمان HTTP|فراخوانی متد تحت شبکه `OpaDataUpdater.PutAsJsonAsync`|
|مصرف‌کننده `DepartmentSoftDeleteConsumer`|(بدون ارتباط — مسیر مرده)|—|—|

**نتیجه‌گیری:** اگرچه استفاده از اوتباکس غیرهمزمان و لایه پیام‌رسانی باعث ایجاد پیوستگی و کاهش وابستگی آنی در فرآیندهای پاکسازی حافظه کش، همگام‌سازی سیاست‌های OPA، سیستم‌های مانیتورینگ و لاگ فعالیت‌ها، و فرآیندهای همگام‌سازی کاربر/مستأجر شده است، اما **فرآیندهای اصلی مرتبط با تصمیم‌گیری‌های دسترسی‌ها و یا واکشی مشخصات نقش‌ها و مستأجرها کاملاً به صورت همزمان ارسال می‌شوند**. بنابراین سیستم در عمل فاقد پایداری و استقلال کامل در زمان اجرا است. لایه پیام‌رسانی در این بستر بیشتر به عنوان یک ابزار کمکی در کنار یک معماری با ارتباطات عمدتاً همزمان (Request/Response) عمل می‌کند.

# ارزیابی تخصصی معماری پلتفرم (Architectural Review)

### مرزهای سرویس‌ها و مالکیت داده‌ها

- **سرویس IdentityService** مالک اصلی این بخش‌ها است: کاربران و چرخه حیات نشست‌ها (رویدادهای دامنه‌ای با قالب‌های `User*V1` و `Session*V1`).
    
- **سرویس TenantService** مالک اصلی این بخش‌ها است: مستأجرها و چرخه حیات دپارتمان‌ها (رویدادهای دامنه‌ای با قالب‌های `Tenant*V1` و `Department*V1`).
    
- **سرویس AuthorizationService** مالک اصلی این بخش‌ها است: نقش‌ها، دسترسی‌ها و رویدادهای ارزیابی عملکرد (رویدادهای دامنه‌ای با قالب‌های `authorization.*.v1`).
    
- تفکیک وظایف کاملاً درست انجام شده و با مفاهیم دامنه‌ای سازگار است. قراردادهای پیام‌رسانی در پکیج دامنه‌ای اختصاصی هر سرویس در آدرس `Domain/Events` تعریف شده و با قرارگیری در داخل کلاس اشتراکی `EventEnvelope` در پروژه `SharedKernel` سریالایز و منتقل می‌شوند.
    

### مالکیت تولیدکننده، مصرف‌کننده و قراردادها

- مالکیت تولیدکننده‌ها: کاملاً شفاف و مشخص است (هر خانواده از رویدادها صرفاً توسط یک سرویس خاص تولید می‌شود).
    
- مالکیت قراردادها: **شیء پوششی مشترک** (`SharedKernel.Contract.Events.EventEnvelope`) به همراه یک **بخش داده‌ای نامشخص و جنریک با قالب خروجی JSON** قرار دارد؛ ماهیت و ساختار فیلد `EventType` به طور منطقی تحت مالکیت سرویس تولیدکننده است، اما **سرویس‌های مصرف‌کننده مقادیر رشته‌ای را به شکل هاردکد درون کدهای خود نوشته‌اند** (مانند استفاده از رشته `"authorization.role-assigned.v1"` در بدنه مصرف‌کننده سرویس هویت یا رشته `"DepartmentSoftDeletedV1"`). در این ساختار **هیچ پکیج یا DLL مشترکی برای قرارداد رویدادها تعریف نشده است** — این موضوع به این معناست که تغییر نام یکی از رویدادها توسط سرویس تولیدکننده، بدون بروز هیچ‌گونه خطای کامپایل، سبب خرابی و از کار افتادن مصرف‌کننده در زمان اجرا خواهد شد. این موضوع یک ریسک جدی در لایه مدیریت قراردادها (Contract Ownership Risk) به حساب می‌آید.
    

### جفت‌شدگی و انتشار گسترده (Coupling & Fan-out)

- **تمامی صف‌ها کپی کاملی از تمامی پیام‌ها را دریافت می‌کنند** (به دلیل استفاده از تک اکسچنج موضوعی مشترک + نوع پیام یکسان در بدنه رابیت‌ام‌کی + کلیدهای مسیریابی کاملاً مشابه). هر پیام ورودی توسط تمام مصرف‌کنندگان دیسریالایز شده و در صورتی که با شروط تعریف شده همخوانی نداشته باشد، نادیده گرفته شده و دور ریخته می‌شود. این رویکرد جفت‌شدگی بسیار بالایی در سطح لایه انتقال شبکه ایجاد کرده و سبب هدررفت منابع پردازشی CPU و پهنای باند I/O می‌شود. در این میان، یک مصرف‌کننده با پیکربندی نامناسب در مواجهه با رویداد غیرمرتبط تنها زمانی دچار خطای استثنا (Exception) می‌شود که فاقد سپرهای حفاظتی و شروط بررسی اولیه باشد (اکثر مصرف‌کنندگان گارد بررسی دارند، اما مواردی مانند `AuditPipelineConsumer`/`AnalyticsPipelineConsumer` تمامی رویدادها را بدون استثنا پردازش می‌کنند).
    

### جزئیات رویدادها و استانداردهای نام‌گذاری

- جزئیات رویدادها به درستی پیاده شده است (یک رویداد به ازای هر بار تغییر در وضعیت اگریگیت‌ها).
    
- استاندارد نام‌گذاری **نامنسجم** است: سرویس‌های هویت و مستأجر از فرمت پاسکال‌کیس به همراه شماره نسخه (`PascalCaseV1`) استفاده می‌کنند (مانند `UserRegisteredV1`، `TenantCreatedV1`)؛ در حالی که سرویس مجوزدهی از استاندارد کباب‌کیس استفاده می‌کند (`kebab-case.v1`) (مانند `authorization.role-assigned.v1`). هیچ قالب استانداردی میان تیم‌ها توافق نشده است.
    
- مشخصه نسخه `Version` در بدنه تمام رویدادها به صورت هاردکد برابر با عدد `1` نوشته شده که همپوشانی **تکراری** با نام نسخه ثبت شده در انتهای هدر `EventTypeName` (با پسوند `V1`) دارد. در ساختار فعلی هیچ سازوکار داینامیکی برای مدیریت نسخه‌ها در طول زمان نوشته نشده است.
    

### توپولوژی رابیت‌ام‌کی، اکسچنج‌ها، روتینگ و مکانیزم‌های بازیابی خطا

- استراتژی اکسچنج‌ها: استفاده از تک اکسچنج موضوعی مشترک — یک پیاده‌سازی ساده اما فاقد ظرافت‌های فنی لازم.
    
- استراتژی صف‌ها: تعریف صف اختصاصی به ازای هر کلاینت؛ سرویس‌های مجوزدهی و مستأجر از قابلیت Quorum و صف‌های نامه مرده (DLQ) استفاده می‌کنند، اما سرویس هویت فاقد آن است.
    
- استراتژی مسیریابی: **اصلاً بکار گرفته نشده است** — کلید مسیریابی پیام‌ها همواره برابر با نام کلاس عمومی `EventEnvelope` است؛ فرآیند فیلتر کردن صرفاً به صورت نرم‌افزاری در سمت کدهای مصرف‌کننده با تطبیق رشته `EventType` انجام می‌شود.
    
- فیلترینگ سمت اپلیکیشن: استفاده از شروط کنترلی سوئیچ به همراه گارد حفاظتی ذخیره‌سازی وضعیت در Redis جهت جلوگیری از اجرای تکراری با طول عمر ماندگاری ۷ روزه.
    
- مکانیزم بازتلاش (Retry): سرویس‌های مجوزدهی و مستأجر از بازتلاش افزایشی نمایی با فرمت متد `UseMessageRetry` استفاده می‌کنند (۱۰ بار تکرار، از ۱ ثانیه تا ۳۰ ثانیه به همراه نوسان تصادفی ۵ ثانیه‌ای)؛ سرویس هویت **فاقد لایه بازتلاش در سطح رابیت‌ام‌کی است**.
    
- مکانیزم مدیریت پیام‌های خطا (Dead-letter): سرویس‌های مجوزدهی و مستأجر از صف‌های خطای رابیت با پسوندهای `.dlx`/`.dlq` استفاده می‌کنند؛ سرویس هویت صرفاً خطاها را به صورت مستقیم در قالب جدول دیتابیس `DeadLetterMessage` با فرآیندهای سیاست اوتباکس ثبت می‌کند.
    
- لایه تلفیقی اوتباکس: این بخش به خوبی، با پایداری کامل و به صورت یکپارچه در هر سه سرویس توسعه داده شده است (تلفیق الگوی Transactional Outbox + فرآیند پردازشگر پس‌زمینه + گارد ابطال تکرارها در Redis). این لایه مستحکم‌ترین و بهترین پیاده‌سازی انجام شده در کل این سناریو معماری است.
    

# ریسک‌های معماری سامانه (Architectural Risks)

### سطح بحرانی (Critical)

1. **مصرف‌کننده `DepartmentSoftDeleteConsumer` کاملاً مرده است / فرآیند حذف دپارتمان به هیچ سرویسی منتقل نمی‌شود.** کدهای مصرف‌کننده در انتظار دریافت رویدادی با نام `DepartmentSoftDeletedV1` هستند که در هیچ بخشی از کدهای سیستم تولید و منتشر نمی‌شود؛ سرویس مستأجر در زمان حذف دپارتمان رویدادی با عنوان `DepartmentStatusChangedV1` ارسال می‌کند. در نتیجه، نقش‌های متصل به بخش‌های حذف شده هرگز در سیستم مجوزدهی غیرفعال نخواهند شد. مستندات اثبات: ساختار کدهای فایل `DepartmentSoftDeleteConsumer.cs:18,24`؛ کلاس `DeleteDepartmentCommandHandler.cs:25`؛ همچنین فایل `TenantDomainEvents.cs` فاقد کلاس رویدادی با نام `DepartmentSoftDeleted` است؛ جستجوی واژه کلیدی `DepartmentSoftDeleted` تنها به نام مصرف‌کننده آن ختم می‌شود.
    

### سطح بالا (High)

2. **سرویس هویت فاقد مکانیزم بازتلاش در سطح گذرگاه یا صف نامه‌های مرده رابیت‌ام‌کی است.** هرگونه اختلال موقت در سیستم‌های مصرف‌کننده سرویس هویت (مانند قطع شدن موقت دیتابیس Redis) با عدم تکرار توسط رابیت‌ام‌کی مواجه شده و دیتای پیام برای همیشه از دست می‌رود، مگر اینکه اوتباکس دیتابیس مجدداً فرآیند ارسال را تریگر کند؛ فرآیند ارسال مجدد اوتباکس نیز صرفاً زمانی به کمک می‌آید که خطایی در مرحله _ارسال_ رخ داده باشد، نه زمانی که فرآیند _دریافت و پردازش پیام توسط مصرف‌کننده_ پس از ثبت تاییدیه با شکست روبرو شود. مستندات اثبات: فایل `ServiceCollectionExtensions.cs:135-160` (فاقد کدهای تعریف `UseMessageRetry` یا `ReceiveEndpoint` و `BindDeadLetterQueue`).
    
3. **نبود پکیج یا DLL مشترک برای مدیریت نسخه‌گذاری قرارداد پیام‌ها.** تمام نام‌های رویدادها به صورت رشته‌های هاردکد و تکراری میان کدهای تولیدکننده و مصرف‌کننده تقسیم شده‌اند. تغییر نام یک متغیر در سمت تولیدکننده، بدون هیچ اخطاری در زمان کامپایل، سیستم مصرف‌کننده را در محیط عملیاتی با از کار افتادگی کامل روبرو خواهد کرد. مستندات اثبات: رشته‌های هاردکد شده در فایل‌های کدهای `AuthorizationCacheInvalidationConsumer.cs:33-66`، `DepartmentSoftDeleteConsumer.cs:18`، و `TenantCacheInvalidationConsumer.cs:27-40`.
    
4. **تضاد ساختار همزمان شبکه با ادعای معماری ناهمزمان پیام‌رسانی.** فرآیند ورود کاربران و تغییرات اطلاعات مستأجرها به صورت مستقیم به درگاه‌های ارتباطی همزمان HTTP با سرویس‌های مجوزدهی و مستأجر وابسته است. با خروج سرویس مجوزدهی از سرویس‌دهی، فرآیند لاگین کاربران و تغییرات مستأجرها در جا با شکست مواجه می‌شود. مستندات اثبات: هندلرهای دستورات سیستم + ساختار کدهای کلاس `AuthorizationBehavior.cs:39`.
    

### سطح متوسط (Medium)

5. **ارسال پیام‌ها به تمام صف‌ها (الگوی ترافیک سنگین)** — هر پیام به تمام صف‌ها فرستاده می‌شود؛ هدر رفتن منابع ترافیکی و توان پردازش؛ نیاز مبرم مصرف‌کنندگان به سپرهای حفاظتی جهت جلوگیری از بروز خطاهای احتمالی.
    
6. **رویدادهای یتیم و بلااستفاده** — پیام‌هایی مانند `TenantNameUpdatedV1` و `Department*UpdatedV1` در دیتابیس اوتباکس تولید می‌شوند اما مصرف‌کننده‌ای برای آن‌ها وجود ندارد؛ در نتیجه کش‌های مرتبط با آن‌ها هرگز پاکسازی نمی‌شوند.
    
7. **فرآیندهای تکراری در ابطال حافظه کش** — هندلرهای اصلی و کدهای مصرف‌کننده رویدادها هر دو به صورت موازی اقدام به ابطال کش دیتابیس می‌کنند.
    
8. **تعریف کلاس‌های رویداد دامنه‌ای مرده یا سناریوهای بلااستفاده شرطی** — کدهایی مانند `UserLoggedInV1`، `MfaVerifiedV1` و در حدود ۸ رویداد مختلف دیگر در کدهای سرویس مجوزدهی تعریف شده‌اند اما هرگز در فرآیندهای واقعی صادر نمی‌شوند (فرسودگی کدهای قدیمی یا توسعه ناقص فیچرهای جدید).
    
9. **عملکرد صوری در پایپلاین‌های Audit و Analytics** — پیاده‌سازی متدهای کلاس‌های `LoggingAuthorizationAuditSink` و `AnalyticsEventSink` صرفاً محدود به نوشتن کدهای لاگینگ ساده است؛ در این خطوط پایپلاین هیچ اطلاعاتی به صورت پایدار و دائم ذخیره نمی‌شود.
    

### سطح پایین (Low)

10. **عدم یکپارچگی در استانداردهای نام‌گذاری رویدادها** (استفاده همزمان از قالب پاسکال‌کیس `PascalCaseV1` و کباب‌کیس `kebab.v1`).
    
11. **فیلد تکراری نسخه `Version`** (که مقدار آن همواره عدد ۱ است) با پسوند از پیش ثبت شده در نام پیام یعنی `V1` همپوشانی دارد؛ فاقد هرگونه استراتژی ارتقای نسخه و نگهداری تغییرات قراردادها در طول زمان.
    
12. **پیام یتیم `UserSynchronizedV1`** — یک رویداد پایانی در فرآیند همگام‌سازی که مصرف‌کننده‌ای ندارد؛ احتمالاً با هدف بکارگیری در سناریوهای ذخیره‌سازی داده‌های بعدی ایجاد شده که در حال حاضر نیمه‌کاره رها شده است.
    

# بدهی‌های فنی پروژه (Technical Debt)

- وجود مصرف‌کننده بلااستفاده و مرده `DepartmentSoftDeleteConsumer` به همراه شاخه‌های مرده شرطی در کدهای مصرف‌کننده (مانند `UserLoggedInV1` و `MfaVerifiedV1`).
    
- ثبت بیش از ۱۲ رویداد دامنه‌ای نیمه‌کاره و رهاشده که هندلرهای اصلی سیستم فاقد اتصال با آن‌ها هستند.
    
- سرویس `UsageAccountingService` در سیستم دپندسی اینجکشن ثبت شده اما در هیچ کجای پروژه‌ها فراخوانی نمی‌شود ← رویداد دامنه‌ای `UsageIncrementedDomainEvent` فاقد کارایی است.
    
- عدم وجود پکیج مشترک برای قراردادها؛ استفاده مکرر از رشته‌های متنی هاردکد شده به جای کدهای ساختاری تعاملی.
    
- عدم برخورداری از معماری و سیاست‌های نسخه‌گذاری برای تغییرات قرارداد پیام‌ها در طول زمان با وجود درج فیلد نسخه در بدنه پیام‌ها.
    
- ناهمگونی و ضعف در پایداری و بازیابی خطاهای لایه اوتباکس و صف‌های رابیت‌ام‌کی در سرویس هویت نسبت به دو سرویس دیگر پروژه.
    
- سیستم‌های صوری و غیرعملیاتی در لایه پایپلاین‌های مانیتورینگ و لاگ‌های امنیتی (صرفاً در حد متدهای نوشتن کدهای لاگ ساده کلاسی).
    

# ریسک‌های محیط عملیاتی (Production Risks)

- **ناهمگونی و عدم انطباق پنهان داده‌ها در دیتابیس:** حذف یک بخش در سیستم مستأجر عملاً نقش‌های متصل به آن دپارتمان را در سرویس مجوزدهی در حالت فعال باقی می‌گذارد (ریسک بحرانی شماره ۱).
    
- **از دست رفتن و گم شدن پیام‌ها در مواجهه با خطای مصرف‌کنندگان سرویس هویت** (به دلیل نبود صف نامه‌های مرده DLQ و سیاست‌های بازتلاش).
    
- **تاثیرات تخریبی آبشاری در زمان قطعی سیستم:** از کار افتادن سرویس مجوزدهی به سرعت فرآیند ورود کاربران در سرویس هویت و ثبت دستورات در سرویس مستأجر را با خطای سیستمی متوقف می‌سازد (وابستگی به تماس‌های همزمان تحت شبکه).
    
- **انحراف اطلاعات در سیاست‌های دسترسی سرویس OPA:** در صورتی که مصرف‌کننده `OpaSyncConsumer` پس از اتمام بازتلاش‌ها با خطا مواجه شود، اطلاعات سیاست‌های دسترسی OPA با دیتابیس اصلی دچار ناهمگونی پنهان می‌شود (صف خطای DLQ پیام معیوب را نگه می‌دارد اما نیاز به مانیتورینگ و بازنشر دستی پیام دارد).
    
- **کهنگی و عدم انطباق اطلاعات حافظه موقت:** عدم ویرایش کلیدهای کش در زمان بروزرسانی مشخصات و نام مستأجرها یا دپارتمان‌ها، دیتای نامعتبر و منقضی‌شده را در کش نگه می‌دارد.
    
- **عدم دسترسی به لاگ‌های وقایع و اطلاعات مانیتورینگ سیستم:** پایپلاین‌های ثبت اطلاعات وقایع و سیستم‌های مانیتورینگ در حال حاضر صرفاً اطلاعات را به صورت لاگ متنی ثبت می‌کنند و هیچ پایداری داده‌ای در سطح دیتابیس یا فایل وجود ندارد.
    

# توصیه‌ها و راهکارهای پیشنهادی

### اقدامات فوری (رفع نقایص و ایرادات جدی کدهای فعلی)

1. **رفع ایراد زنجیره حذف بخش‌ها در سیستم.** برای حل این موضوع دو مسیر پیشنهاد می‌شود: الف) سرویس TenantService در زمان حذف بخش، رویداد `DepartmentSoftDeletedV1` را صادر کند (یا رویداد `DepartmentStatusChangedV1` را با اعمال وضعیت فیلتر شده مناسب ارسال کند تا مصرف‌کننده متناظر فعال شود)، یا ب) کدهای مصرف‌کننده `DepartmentSoftDeleteConsumer` را بگونه‌ای ویرایش کنید تا پیام `DepartmentStatusChangedV1` را دریافت کرده و با شرط فیلتر وضعیت حذف‌شده پردازش را پیش ببرد. این اصلاحیه فرآیند غیرفعال‌سازی نقش‌ها را در زمان حذف دپارتمان احیا می‌کند. _(سطح اهمیت: بحرانی)_
    
2. **کدهای پیکربندی `UseMessageRetry` به همراه ویژگی Quorum و صف‌های نامه مرده (DLQ) را به سرویس هویت اضافه کنید** تا پایداری و بازیابی پیام‌ها همگام با سرویس‌های مستأجر و مجوزدهی ارتقا یابد.
    
3. **شاخه‌های شرطی مرده و رها شده را پاک کنید** (مانند کدهای `UserLoggedInV1` و `MfaVerifiedV1`) یا تولیدکنندگان مناسب را برای هدایت داده‌ها به آن‌ها بنویسید؛ کدهای کلاس `DepartmentSoftDeleteConsumer` را تنها زمانی در مدار قرار دهید که دیتای ورودی آن به درستی تولید و منتشر شود.
    

### اقدامات آتی (افزایش پایداری و مستحکم‌سازی معماری)

4. **یک پروژه یا پکیج اشتراکی اختصاصی برای رویدادهای یکپارچه‌سازی (Integration Events) ایجاد کنید** (شامل نگهداری کلاس‌های اصلی پیام‌ها و ثوابت متنی نام رویدادها) تا تغییرات دیتای پیام‌ها در زمان کامپایل توسط پروژه‌ها اعتبارسنجی شده و از بروز خطاهای پنهان کدهای هاردکد جلوگیری به عمل آید.
    
5. **از قابلیت مسیریابی موضوعی (Topic Routing) رابیت‌ام‌کی استفاده کنید** — پیام‌ها را با کلیدهای روتینگ اختصاصی بر اساس نوع هر رویداد منتشر کنید و صف‌ها را منحصراً به دیتای مورد نیاز متصل سازید؛ این فرآیند از ارسال کپی تمام پیام‌ها به تمامی صف‌های سیستم جلوگیری می‌کند (صرفه‌جویی در پردازش I/O و کاهش بار شبکه سرویس‌ها).
    
6. **فرمت نام‌گذاری رویدادها را یکپارچه کنید** (مانند استفاده همگانی از استاندارد کباب‌کیس به شکل `kebab-case.vN`) و استراتژی نسخه‌گذاری و ارتقای نسخه‌ها را با استفاده صحیح از فیلد نسخه موجود در پیام‌ها پایه‌ریزی کنید.
    
7. **پایپلاین‌های مانیتورینگ و لاگ فعالیت‌ها را به دیتابیس‌های پایدار متصل سازید** (داده‌ها را در قالب ابزارهای لاگینگ تخصصی ذخیره کنید) یا به صورت رسمی در مستندات بیاورید که این بخش‌ها صرفاً جهت دیباگ متنی توسعه داده شده‌اند.
    
8. **تعاملات همزمان و فشرده تحت شبکه را در لایه تصمیم‌گیری‌های مجوزدهی کاهش دهید** (به عنوان مثال از طریق همگام‌سازی و نگهداری حافظه کش سیاست‌ها در لایه محلی یا فرآیندهای محاسباتی غیرهمزمان قبلی) تا پایداری زمان اجرای سرویس‌ها به صورت مستقل به معنای واقعی تامین گردد.
    
9. **وضعیت رویدادهای یتیم و بلااستفاده را تعیین تکلیف کنید** (مانند رویدادهای `TenantNameUpdatedV1`، `Department*UpdatedV1`، `SessionCreatedV1` و غیره) — مصرف‌کنندگان مناسب را برای آن‌ها بنویسید یا فرآیند انتشار آن‌ها را در دیتابیس اوتباکس کدهای مربوطه متوقف سازید.
    

### اقدامات ممنوعه (کارهایی که نباید انجام شوند)

- به **هیچ عنوان** سراغ پیاده‌سازی ابزارهای ارکستراسیون یا Sagaها نروید تا زمانی که ایرادات کدهای فعلی و رویدادهای مرده و یتیم برطرف شوند — در حال حاضر زنجیره‌های واکنشی سیستم دچار ایرادهای پایه‌ای است.
    
- به **هیچ عنوان** تعداد صف‌های سیستم را در اکسچنج مشترک فعلی افزایش ندهید بدون اینکه مکانیزم تفکیک پیام‌ها از طریق روتینگ رابیت‌ام‌کی را اجرایی کرده باشید.
    
- به **هیچ عنوان** تصمیمات مربوط به لایه مجوزدهی را همزمان به شبکه ناهمزمان بسپارید در حالی که ارتباط همزمان HTTP کلاینت‌ها همچنان برقرار است — یکی از این دو مدل را به عنوان الگوی اصلی انتخاب و بقیه را متوقف سازید.
    

_پایان گزارش فنی. تمامی مستندات و یافته‌های این گزارش با تکیه بر اطلاعات و ارجاعات دقیق به آدرس خط کدهای اصلی تهیه شده است. هیچ کدی در مخزن پروژه تغییر نیافته است._