using PolicyService.Domain.Enums;
using PolicyService.Domain.Events;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.Aggregates.Policies;

/// <summary>
/// An ABAC policy definition: conditions + Rego expression. Answers "under what conditions" a
/// permitted action is allowed (AuthorizationService answers permission). Separate aggregate from
/// Subscription / QuotaPolicy (different lifecycle). Published policies are immutable.
/// </summary>
public sealed class Policy : AggregateRoot
{
    private Policy() { }

    private Policy(Guid id, Guid tenantId, string name, PolicyCondition condition, PolicyExpression expression, PolicyPriority priority, PolicyStatus status)
        : base(id, tenantId)
    {
        Name = name;
        Condition = condition;
        Expression = expression;
        Priority = priority;
        Status = status;
    }

    public string Name { get; private set; } = string.Empty;
    public PolicyCondition Condition { get; private set; } = null!;
    public PolicyExpression Expression { get; private set; } = null!;
    public PolicyPriority Priority { get; private set; } = null!;
    public PolicyStatus Status { get; private set; }
    public RegoModule? CompiledRego { get; private set; }

    public static Result<Policy> Create(Guid tenantId, string name, PolicyCondition condition, PolicyExpression expression, PolicyPriority priority, Guid correlationId)
    {
        if (tenantId == Guid.Empty)
            return Result<Policy>.Failure(PolicyErrors.TenantMismatch);
        if (string.IsNullOrWhiteSpace(name))
            return Result<Policy>.Failure(PolicyErrors.PolicyNameRequired);
        if (condition is null)
            return Result<Policy>.Failure(PolicyErrors.PolicyConditionRequired);
        if (expression is null)
            return Result<Policy>.Failure(PolicyErrors.PolicyExpressionRequired);

        var policy = new Policy(Guid.NewGuid(), tenantId, name.Trim(), condition, expression, priority, PolicyStatus.Draft);
        policy.RaiseDomainEvent(new PolicyCreatedDomainEvent(PolicyId.From(policy.Id), tenantId, policy.Name, correlationId));
        return Result<Policy>.Success(policy);
    }

    public Result<Unit> Publish(Guid correlationId)
    {
        if (Status != PolicyStatus.Draft)
            return Result<Unit>.Failure(PolicyErrors.InvalidPolicyLifecycle);
        Status = PolicyStatus.Published;
        MarkUpdated();
        RaiseDomainEvent(new PolicyPublishedDomainEvent(PolicyId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Archive(Guid correlationId)
    {
        if (Status == PolicyStatus.Archived)
            return Result<Unit>.Success(Unit.Value);
        Status = PolicyStatus.Archived;
        MarkUpdated();
        RaiseDomainEvent(new PolicyArchivedDomainEvent(PolicyId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> ChangeCondition(PolicyCondition condition, PolicyExpression expression, Guid correlationId)
    {
        // Published policies are immutable (invariant 11); change requires a new policy.
        if (Status == PolicyStatus.Published)
            return Result<Unit>.Failure(PolicyErrors.InvalidPolicyLifecycle);
        if (condition is null || expression is null)
            return Result<Unit>.Failure(PolicyErrors.PolicyConditionRequired);

        Condition = condition;
        Expression = expression;
        MarkUpdated();
        RaiseDomainEvent(new PolicyConditionChangedDomainEvent(PolicyId.From(Id), TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> SetCompiledRego(RegoModule rego)
    {
        if (rego is null)
            return Result<Unit>.Failure(PolicyErrors.RegoSourceRequired);
        CompiledRego = rego;
        MarkUpdated();
        return Result<Unit>.Success(Unit.Value);
    }

    public PolicyId GetPolicyId() => PolicyId.From(Id);
}
