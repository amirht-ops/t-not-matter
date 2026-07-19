# امنیت و بدهی فنی — پلتفرم امنیت سازمانی

---

## ۱. خلاصه آسیب‌پذیری‌ها

| اولویت | تعداد | زمان تخمینی رفع |
|---------|--------|-----------------|
| CRITICAL | ۳ | ۶ روز |
| HIGH | ۱۱ | ۴.۵ روز |
| MEDIUM | ۲۲ | متغیر |
| LOW | ۱۸ | متغیر |
| **مجموع** | **۵۴** | **~۲۸.۵ روز** |

---

## ۲. آسیب‌پذیری‌های CRITICAL

### CRITICAL-01: جریان MFA شکسته

**فایل:** `IdentityService.Api/Extensions/ServiceCollectionExtensions.cs`
**مشکل:** صفحه `/mfa/verify` به JWT نیاز دارد (RequireAuthorization)، اما کاربرانی که در حالت MFA قرار دارند هیچ توکنی ندارند.

**جریان شکسته:**
```
کاربر → /login → دریافت MFA Challenge → /mfa/verify → 401 (توکن ندارد!)
```

**راه‌حل پیشنهادی:** MFA Challenge Token — یک JWT کوتاه‌مدت (۵ دقیقه) تک‌بار مصرف با claim `mfa_pending`، ذخیره در Redis.

**زمان تخمینی:** ۲ روز

---

### CRITICAL-02: آسیب‌پذیری Code Injection در RegoGenerator

**فایل:** `PolicyService.Infrastructure/Policy/RegoGenerator.cs`
**مشکل:** مقادیر condition در Rego بدون sanitization درج می‌شوند:

```csharp
// آسیب‌پذیر
var condition = $"input.{rule.Condition.Attribute} {rule.Condition.Operator} \"{rule.Condition.Value}\"";
// اگر مقدار شامل کد مخرب باشد → اجرای کد دلخواه در OPA
```

**راه‌حل پیشنهادی:** Sanitize کردن مقادیر condition یا تغییر به OPA data model (`/v1/data/`) به جای text injection.

**زمان تخمینی:** ۱ روز

---

### CRITICAL-03: فقدان اجرای عدم تغییر Audit Log

**فایل:** `AuditService.Domain/Aggregates/AuditRecord.cs`
**مشکل:** لاگ‌های audit فاقد مکانیزم append-only هستند:
- بدون پرچم `IsSealed`
- بدون سیاست‌های PostgreSQL RLS
- بدون محدودیت‌های UPDATE/DELETE در دیتابیس
- بدون hash chaining

**راه‌حل پیشنهادی:**
1. اضافه کردن `IsSealed` به AuditRecord
2. سیاست‌های PostgreSQL RLS که UPDATE/DELETE را رد می‌کنند
3. جداول append-only
4. Hash chaining برای یکپارچگی

**زمان تخمینی:** ۳ روز

---

## ۳. آسیب‌پذیری‌های HIGH

### HIGH-01: دسترسی مستقیم DbContext از لایه API

**فایل:** `IdentityService.Api/Extensions/ServiceCollectionExtensions.cs:108-123`
**مشکل:** اعتبارسنجی نشست مستقیماً از DbContext انجام می‌شود.
**زمان تخمینی:** ۰.۵ روز

### HIGH-02: ChangeDepartment فاقد بررسی مجوز

**فایل:** `IdentityService.Application/Features/ChangeDepartment/`
**مشکل:** دستور ChangeDepartment هیچ بررسی مجوزی انجام نمی‌دهد.
**زمان تخمینی:** ۰.۵ روز

### HIGH-03: TenantStatusChangedConsumer فاقد تراکنش

**فایل:** `IdentityService.Infrastructure/Messaging/Consumers/TenantStatusChangedConsumer.cs`
**مشکل:** مصرف‌کننده تغییر وضعیت Tenant بدون تراکنش اجرا می‌شود.
**زمان تخمینی:** ۰.۵ روز

### HIGH-04: Slug endpoint از MediatR عبور می‌کند

**فایل:** `TenantService.Api/Endpoints/`
**مشکل:** endpoint slug مستقیماً `ITenantRepository` را تزریق می‌کند و از MediatR/CQRS عبور می‌کند.
**زمان تخمینی:** ۱ روز

### HIGH-05: AuthorizationClient دیکشنری‌های خالی ارسال می‌کند

**فایل:** `Platform.Authorization/`
**مشکل:** `AuthorizationServiceClient` دیکشنری‌های خالی `ResourceAttributes`, `EnvironmentAttributes`, `UsageAttributes` ارسال می‌کند.
**زمان تخمینی:** ۱ روز

### HIGH-06: RefreshToken tenant_slug خالی

**فایل:** `IdentityService.Application/Features/RefreshToken/RefreshTokenCommandHandler.cs`
**مشکل:** `tenant_slug` خالی در JWT عبور داده می‌شود.
**زمان تخمینی:** ۰.۵ روز

### HIGH-07: SimulatePolicy خطاها را بلعید

**فایل:** `PolicyService.Application/Features/SimulatePolicy/`
**مشکل:** `catch { return true; }` — تمام خطاها بلعیده می‌شوند.
**زمان تخمینی:** ۰.۵ روز

### HIGH-08: Domain interface در لایه Application

**فایل:** `PolicyService.Application/Features/ValidatePolicy/`
**مشکل:** از `IRegoGenerator` (domain) مستقیماً در Application استفاده می‌شود.
**زمان تخمینی:** ۰.۵ روز

### HIGH-09: TenantService متدهای async جعلی

**فایل:** `TenantService.Infrastructure/`
**مشکل:** متدهای repository به صورت جعلی async هستند.
**زمان تخمینی:** ۰.۵ روز

### HIGH-10: بدون محدودیت نرخ روی login

**فایل:** `IdentityService.Api/`
**مشکل:** endpoint login بدون rate limiting.
**زمان تخمینی:** ۰.۵ روز

### HIGH-11: کلید JWT hardcoded

**فایل:** `IdentityService.Api/`
**مشکل:** کلید امضای JWT به صورت پیش‌فرض hardcoded است.
**زمان تخمینی:** ۰.۵ روز

---

## ۴. آسیب‌پذیری‌های MEDIUM (لیست)

| کد | مشکل | سرویس |
|-----|-------|--------|
| MED-01 | `GetSubjectIdsByRoleAsync` فاقد پارامتر explicit tenantId | Authorization |
| MED-02 | `GetByIdAsync` در DepartmentRepository فاقد tenantId | Authorization |
| MED-03 | `policyName` در URL OPA encode نشده | Policy |
| MED-04 | `DeletePolicy` رویداد domenی تولید نمی‌کند | Policy |
| MED-05 | `TenantNameUpdatedEvent` از TenantId به جای Id استفاده می‌کند | Tenant |
| MED-06 | `IRequestContextAccessor` Singleton اما HttpContext per-request | Tenant |
| MED-07 | `ExistsByCodeAsync` cache false-positive برای tenant حذف شده | Tenant |
| MED-08 | `SimulatePolicy` پیاده‌سازی اسباب‌بازی | Policy |
| MED-09 | مقادیر hardcoded `subject_status=active` | Policy |
| MED-10 | `RoleName` equality با `ToUpperInvariant()` اما query case-sensitive | Authorization |
| MED-11 | `DepartmentRepository.GetByIdAsync` فاقد tenantId | Authorization |
| MED-12 | `AuditEventConsumer` UserId/SubjectId را drop می‌کند | Audit |
| MED-13 | Query endpoints همیشه `Guid.Empty` برای CorrelationId | Audit |
| MED-14 | `DeleteDepartment` رویداد domenی تولید نمی‌کند | Tenant |
| MED-15 | `DepartmentName.GetEqualityComponents` no-op | Tenant |
| MED-16 | `SimulatePolicy` OPA را فراخوانی نمی‌کند | Policy |
| MED-17 | hardcoded subject/tenant status در AuthorizationServiceClient | Policy |
| MED-18 | `GetSubjectIdsByRoleAsync` بدون explicit tenantId filter | Authorization |
| MED-19 | `RoleName` case sensitivity mismatch | Authorization |
| MED-20 | `DepartmentRepository` lacks tenant scoping | Authorization |
| MED-21 | `RefreshTokenCommandHandler` passes empty tenant_slug | Identity |
| MED-22 | `SimulatePolicy` swallows all exceptions | Policy |

---

## ۵. بدهی فنی (Technical Debt)

### بدهی معماری

| مورد | توضیح | اولویت |
|------|-------|--------|
| Delegation Service | README اشاره دارد اما وجود ندارد | P0 |
| Specification Pattern | Filtering inline در repository | P2 |
| Projection Queries | AuditService سنگین‌ترین مسیر خواندن | P2 |
| AuthorizationClient context | دیکشنری‌های خالی | P0 |

### بدهی تست

| مورد | پوشش فعلی | هدف |
|------|-----------|-----|
| Domain Tests | قوی | ۹۵٪ |
| Architecture Tests | ۷۷ تست PASS | حفاظت |
| Application Tests | ~۰٪ | ۸۰٪ |
| Integration Tests | ~۰٪ | ۸۰٪ |
| E2E Tests | ~۰٪ | ۱۰۰٪ |

### بدهی عملیاتی

| مورد | وضعیت فعلی | هدف |
|------|-----------|-----|
| OpenTelemetry | صفر | Traces + Metrics + Logs |
| Grafana Dashboards | صفر | Dashboard per service |
| Loki | صفر | Log aggregation |
| Tempo | صفر | Trace store |
| Prometheus | صفر | Metrics endpoint |
| Kubernetes manifests | صفر | Deployment, Service, Ingress |
| Helm charts | صفر | Per-env values |
| SLO/SLA | صفر | Definition per service |
| Backup strategy | صفر | Runbook |

---

## ۶. OWASP Top 10 برای .NET

| OWASP | وضعیت | توضیح |
|-------|--------|-------|
| A01:Broken Access Control | بحرانی | HIGH-02, HIGH-04, HIGH-05 |
| A02:Cryptographic Failures | متوسط | HIGH-11 (hardcoded JWT key) |
| A03:Injection | بحرانی | CRITICAL-02 (Rego injection) |
| A04:Insecure Design | متوسط | CRITICAL-03 (audit immutability) |
| A05:Security Misconfiguration | متوسط | HIGH-10 (no rate limiting) |
| A06:Vulnerable Components | خوب | .NET 10 (latest) |
| A07:Auth Failures | بحرانی | CRITICAL-01 (MFA broken) |
| A08:Data Integrity Failures | متوسط | CRITICAL-03 (audit) |
| A09:Logging Failures | خوب | AuditService exists |
| A10:SSRF | خوب | OPA communication only |

---

## ۷. اولویت‌بندی رفع

### فاز P0 (۱۰.۵ روز)

1. رفع CRITICAL-01: MFA Challenge Token
2. رفع CRITICAL-02: RegoGenerator sanitization
3. رفع CRITICAL-03: Audit immutability
4. رفع HIGH-02: ChangeDepartment authorization
5. رفع HIGH-03: Transaction wrapping
6. رفع HIGH-10: Rate limiting
7. رفع HIGH-11: JWT key validation

### فاز P1 (۹ روز)

1. MediatR برای Slug endpoint
2. AuthorizationClient context population
3. ترکیب interface های تکراری
4. رفع HIGH-06: RefreshToken tenant_slug
5. رفع HIGH-07: SimulatePolicy error handling
6. رفع HIGH-08: Domain interface in Application
