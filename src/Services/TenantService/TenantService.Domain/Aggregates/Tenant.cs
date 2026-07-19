using SharedKernel.Domain.Primitives;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Results;
using TenantService.Domain.Enums;
using TenantService.Domain.Errors;
using TenantService.Domain.Events;
using TenantService.Domain.ValueObjects;

namespace TenantService.Domain.Aggregates;

public sealed class Tenant : AggregateRoot
{
    private Tenant() { }

    private Tenant(Guid id, Guid tenantId, TenantCode tenantCode, string name,
                   TenantSlug slug, TenantStatus status, PlanTier planTier)
        : base(id, tenantId)
    {
        Code = tenantCode;
        Name = name;
        Slug = slug;
        Status = status;
        PlanTier = planTier;
    }

    public TenantCode Code { get; private set; }
    public string Name { get; private set; }
    public TenantSlug Slug { get; private set; }
    public TenantStatus Status { get; private set; }
    public PlanTier PlanTier { get; private set; }
    public string Identifier => Code.Value;

    public bool IsActive => Status == TenantStatus.Active;
    public bool IsSuspended => Status == TenantStatus.Suspended;
    public bool IsDisabled => Status == TenantStatus.Disabled;

    public static Result<Tenant> Create(string name, string identifier, TenantSlug slug, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<Tenant>.Failure(TenantErrors.NameRequired);

        if (name.Length > 200)
            return Result<Tenant>.Failure(TenantErrors.NameTooLong);

        var codeResult = TenantCode.FromString(identifier);
        if (codeResult.IsFailure)
            return Result<Tenant>.Failure(TenantErrors.InvalidIdentifier);

        var tenantCode = codeResult.Value!;
        var id = Guid.NewGuid();

        var tenant = new Tenant(id, id, tenantCode, name.Trim(), slug, TenantStatus.Pending, PlanTier.Free);
        tenant.RaiseDomainEvent(new TenantCreatedEvent(id, tenantCode.Value, name.Trim(), slug.Value, correlationId));
        return Result<Tenant>.Success(tenant);

    }

    public Result<Unit> Activate(Guid correlationId)
    {
        if (Status == TenantStatus.Active)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        if (Status == TenantStatus.Disabled)
        {
            return Result<Unit>.Failure(TenantErrors.TenantIsDisabled);
        }

        var previousStatus = Status.ToString();
        Status = TenantStatus.Active;
        MarkUpdated();
        RaiseDomainEvent(new TenantStatusChangedEvent(Id, previousStatus, Status.ToString(), correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Deactivate(Guid correlationId)
    {
        if (Status == TenantStatus.Disabled)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        if (Status == TenantStatus.Pending)
        {
            return Result<Unit>.Failure(TenantErrors.InvalidStatusTransition);
        }

        var previousStatus = Status.ToString();
        Status = TenantStatus.Disabled;
        MarkUpdated();
        RaiseDomainEvent(new TenantStatusChangedEvent(Id, previousStatus, Status.ToString(), correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> SuspendForNonPayment(Guid correlationId)
    {
        if (Status == TenantStatus.Suspended)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        if (Status is TenantStatus.Disabled or TenantStatus.Pending)
        {
            return Result<Unit>.Failure(TenantErrors.InvalidStatusTransition);
        }

        var previousStatus = Status.ToString();
        Status = TenantStatus.Suspended;
        MarkUpdated();
        RaiseDomainEvent(new TenantStatusChangedEvent(Id, previousStatus, Status.ToString(), correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> UpgradePlan(PlanTier newPlanTier, Guid correlationId)
    {
        if (PlanTier == newPlanTier)
        {
            return Result<Unit>.Failure(TenantErrors.SamePlanTier);
        }

        if (Status == TenantStatus.Disabled)
        {
            return Result<Unit>.Failure(TenantErrors.CannotUpgradeFromDisabled);
        }

        if (Status == TenantStatus.Suspended)
        {
            return Result<Unit>.Failure(TenantErrors.CannotUpgradeFromSuspended);
        }

        if (newPlanTier <= PlanTier)
        {
            return Result<Unit>.Failure(TenantErrors.SamePlanTier);
        }

        var previousPlanTier = PlanTier.ToString();
        PlanTier = newPlanTier;
        MarkUpdated();
        RaiseDomainEvent(new TenantPlanUpgradedEvent(Id, previousPlanTier, PlanTier.ToString(), correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> UpdateName(string newName, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Result<Unit>.Failure(TenantErrors.NameRequired);
        }

        if (newName.Length > 200)
        {
            return Result<Unit>.Failure(TenantErrors.NameTooLong);
        }

        var trimmed = newName.Trim();
        if (Name == trimmed)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var previousName = Name;
        Name = trimmed;
        MarkUpdated();
        RaiseDomainEvent(new TenantNameUpdatedEvent(TenantId, previousName, trimmed, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }
}
