# Policy Service — Event Catalog

Convention (from `AuthorizationService.Domain` / `IdentityService.Domain`):
```csharp
public abstract record PolicyDomainEvent(Guid TenantId, Guid CorrelationId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid? CausationId { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public int Version { get; init; } = 1;
    public abstract string EventTypeName { get; }
}
```
Events carry `TenantId` + `CorrelationId` only (ADR-012). Audit-relevant events → AuditService via Outbox (ADR-017).

## Policy aggregate

| Event | EventTypeName | Payload (besides TenantId/CorrelationId) | Audit? |
|-------|---------------|-------------------------------------------|--------|
| `PolicyCreated` | `policy.policy-created.v1` | PolicyId, Name, Priority | yes |
| `PolicyPublished` | `policy.policy-published.v1` | PolicyId, Version, EffectiveFrom | yes |
| `PolicyArchived` | `policy.policy-archived.v1` | PolicyId | yes |
| `PolicyConditionChanged` | `policy.policy-condition-changed.v1` | PolicyId, Version | yes |

## Subscription aggregate

| Event | EventTypeName | Payload | Audit? |
|-------|---------------|---------|--------|
| `SubscriptionAssigned` | `policy.subscription-assigned.v1` | SubscriptionId, PolicyId, Scope | yes |
| `SubscriptionActivated` | `policy.subscription-activated.v1` | SubscriptionId, EffectiveFrom | yes |
| `SubscriptionRevoked` | `policy.subscription-revoked.v1` | SubscriptionId | yes |
| `SubscriptionSuperseded` | `policy.subscription-superseded.v1` | SubscriptionId, SupersededBy | yes |

## QuotaPolicy aggregate

| Event | EventTypeName | Payload | Audit? |
|-------|---------------|---------|--------|
| `QuotaPolicyDefined` | `policy.quota-policy-defined.v1` | QuotaPolicyId, Scope, Quota(Daily,Weekly,Monthly) | yes |
| `QuotaPolicyAmended` | `policy.quota-policy-amended.v1` | QuotaPolicyId, NewQuota | yes |
| `QuotaPolicyRemoved` | `policy.quota-policy-removed.v1` | QuotaPolicyId | yes |

## UsageLedger / DebtLedger aggregates

| Event | EventTypeName | Payload | Audit? |
|-------|---------------|---------|--------|
| `UsageRecorded` | `policy.usage-recorded.v1` | ConsumerId, ActionKey, Window, Count | no |
| `UsageReset` | `policy.usage-reset.v1` | ConsumerId, Scope, Reason(WindowRollover\|Admin) | yes |
| `DebtIncurred` | `policy.debt-incurred.v1` | ConsumerId, ActionKey, Window, Amount | yes |
| `DebtRecovered` | `policy.debt-recovered.v1` | ConsumerId, ActionKey, Amount, Remaining | no |
| `DebtReset` | `policy.debt-reset.v1` | ConsumerId, Scope, Reason(Admin) | yes |
| `QuotaExceeded` | `policy.quota-exceeded.v1` | ConsumerId, ActionKey, Window, Excess | yes |
| `OperationAllowed` | `policy.operation-allowed.v1` | ConsumerId, ActionKey, DecisionContext | no |
| `OperationDenied` | `policy.operation-denied.v1` | ConsumerId, ActionKey, Reason(DebtBlock\|MonthlyDebtDominance) | no |

## Consumed external events (to hydrate read models — NOT domain events)

| Source | Event | Used for |
|--------|-------|----------|
| AuthorizationService | `authorization.role-assigned.v1`, `authorization.role-department-linked.v1` | principal-hierarchy read model (Role→Department) |
| TenantService | `tenant.created.v1` | Tenant existence ref |
| IdentityService | `identity.user-created.v1` | ConsumerId ref |

PolicyService **does not** emit events that other services consume for identity/RBAC; it only consumes for local read models and emits audit + OPA-distribution events.

## Notes
- `OperationAllowed` / `OperationDenied` are **derived verdicts** emitted to the OPA/distribution layer, not used for accounting.
- `QuotaExceeded` is informational (debt already recorded by `DebtIncurred`); never encode quota in Rego (ADR-018).
