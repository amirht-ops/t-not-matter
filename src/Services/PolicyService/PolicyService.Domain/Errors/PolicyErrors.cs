using SharedKernel.Errors;

namespace PolicyService.Domain.Errors;

public static class PolicyErrors
{
    // Policy errors
    public static readonly Error PolicyNameRequired =
        Error.Validation("Policy.NameRequired", "Policy name is required.");

    public static readonly Error PolicyNotFound =
        Error.NotFound("Policy.NotFound", "The requested policy was not found.");

    public static readonly Error InvalidPolicyLifecycle =
        Error.Validation("Policy.InvalidLifecycle", "Invalid policy lifecycle transition.");

    public static readonly Error PolicyConditionRequired =
        Error.Validation("Policy.ConditionRequired", "A policy condition is required.");

    public static readonly Error PolicyExpressionRequired =
        Error.Validation("Policy.ExpressionRequired", "A policy expression is required.");

    public static readonly Error PolicyNotPublished =
        Error.Failure("Policy.NotPublished", "Only published policies can be subscribed.");

    public static readonly Error RegoSourceRequired =
        Error.Validation("Policy.RegoSourceRequired", "Rego source is required.");

    // Subscription errors
    public static readonly Error SubscriptionNotFound =
        Error.NotFound("Subscription.NotFound", "The requested subscription was not found.");

    public static readonly Error InvalidSubscriptionLifecycle =
        Error.Validation("Subscription.InvalidLifecycle", "Invalid subscription lifecycle transition.");

    public static readonly Error SubscriptionScopeRequired =
        Error.Validation("Subscription.ScopeRequired", "A subscription scope is required.");

    public static readonly Error PolicyAlreadySubscribed =
        Error.Conflict("Subscription.AlreadySubscribed", "A subscription already exists for this scope and policy.");

    // Quota policy errors
    public static readonly Error QuotaPolicyNotFound =
        Error.NotFound("QuotaPolicy.NotFound", "The requested quota policy was not found.");

    public static readonly Error QuotaLimitInvalid =
        Error.Validation("QuotaPolicy.LimitInvalid", "Quota limit must be non-negative.");

    public static readonly Error QuotaWindowInconsistent =
        Error.Validation("QuotaPolicy.WindowInconsistent", "Monthly limit must be greater than or equal to weekly, and weekly greater than or equal to daily.");

    public static readonly Error QuotaPolicyScopeRequired =
        Error.Validation("QuotaPolicy.ScopeRequired", "A quota policy scope is required.");

    // Usage errors
    public static readonly Error ConsumerIdRequired =
        Error.Validation("Usage.ConsumerRequired", "A consumer id is required.");

    public static readonly Error ActionKeyRequired =
        Error.Validation("Usage.ActionRequired", "An action key is required.");

    public static readonly Error NegativeCount =
        Error.Validation("Usage.NegativeCount", "Usage count cannot be negative.");

    public static readonly Error InvalidWindowRange =
        Error.Validation("Usage.InvalidWindowRange", "Window start must be before window end.");

    public static readonly Error UsageLedgerNotFound =
        Error.NotFound("Usage.LedgerNotFound", "The usage ledger was not found.");

    // Debt errors
    public static readonly Error DebtAmountInvalid =
        Error.Validation("Debt.AmountInvalid", "Debt amount must be non-negative.");

    public static readonly Error DebtLedgerNotFound =
        Error.NotFound("Debt.LedgerNotFound", "The debt ledger was not found.");

    // Consumption / decision errors
    public static readonly Error ConsumedUnitsMustBePositive =
        Error.Validation("Consumption.UnitsMustBePositive", "Consumed units must be greater than zero.");

    public static readonly Error ConsumptionBlockedByDebt =
        Error.Failure("Consumption.BlockedByDebt", "Consumption is blocked because outstanding debt exists.");

    // Shared
    public static readonly Error TenantMismatch =
        Error.Failure("Policy.TenantMismatch", "Tenant mismatch.");

    public static readonly Error ScopePrincipalRequired =
        Error.Validation("Scope.PrincipalRequired", "A scope principal id is required.");

    public static readonly Error ScopeKindInvalid =
        Error.Validation("Scope.KindInvalid", "The scope kind is not a recognized principal kind.");

    public static readonly Error ResourceTypeRequired =
        Error.Validation("Resource.TypeRequired", "A resource type is required.");

    public static readonly Error ResourceIdRequired =
        Error.Validation("Resource.IdRequired", "A resource id is required.");
}
