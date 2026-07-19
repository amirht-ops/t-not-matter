using AuthorizationService.Domain.Errors;
using AuthorizationService.Domain.Events;
using AuthorizationService.Domain.ValueObjects;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.Aggregates.Permission;

public enum PermissionLifecycle
{
    Draft = 1,
    Review = 2,
    Approved = 3,
    Published = 4,
    Deprecated = 5,
    Archived = 6
}

public sealed class Permission : AggregateRoot
{
    private Permission() { }

    private Permission(Guid id, Guid tenantId, PermissionKey key, AuthorizationAction action, string resourceType, string? description, string ownerTeam) : base(id, tenantId)
    {
        Key = key;
        Action = action;
        ResourceType = resourceType;
        Description = description;
        OwnerTeam = ownerTeam;
        Lifecycle = PermissionLifecycle.Draft;
        PermissionVersion = 1;
    }

    public PermissionKey Key { get; private set; }
    public AuthorizationAction Action { get; private set; }
    public string ResourceType { get; private set; }
    public string? Description { get; private set; }
    public string OwnerTeam { get; private set; }
    public PermissionLifecycle Lifecycle { get; private set; }
    public int PermissionVersion { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? DeprecatedAt { get; private set; }
    public bool IsActive => Lifecycle == PermissionLifecycle.Published;

    public static Result<Permission> Create(Guid tenantId, PermissionKey key, AuthorizationAction action, string resourceType, string? description, string ownerTeam, Guid correlationId)
    {
        var permission = new Permission(Guid.NewGuid(), tenantId, key, action, resourceType, description, ownerTeam);
        permission.RaiseDomainEvent(new PermissionCreatedDomainEvent(permission.Id, tenantId, key.Value, correlationId));
        return Result<Permission>.Success(permission);
    }

    public Result<Unit> SubmitForReview(Guid correlationId)
    {
        if (Lifecycle != PermissionLifecycle.Draft)
            return Result<Unit>.Failure(AuthorizationErrors.InvalidPermissionLifecycle);

        Lifecycle = PermissionLifecycle.Review;
        MarkUpdated();
        RaiseDomainEvent(new PermissionSubmittedForReviewDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Approve(Guid correlationId)
    {
        if (Lifecycle != PermissionLifecycle.Review)
            return Result<Unit>.Failure(AuthorizationErrors.InvalidPermissionLifecycle);

        Lifecycle = PermissionLifecycle.Approved;
        MarkUpdated();
        RaiseDomainEvent(new PermissionApprovedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Publish(Guid correlationId)
    {
        if (Lifecycle != PermissionLifecycle.Approved)
            return Result<Unit>.Failure(AuthorizationErrors.InvalidPermissionLifecycle);

        Lifecycle = PermissionLifecycle.Published;
        PublishedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        RaiseDomainEvent(new PermissionPublishedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Deprecate(Guid correlationId)
    {
        if (Lifecycle != PermissionLifecycle.Published)
            return Result<Unit>.Failure(AuthorizationErrors.InvalidPermissionLifecycle);

        Lifecycle = PermissionLifecycle.Deprecated;
        DeprecatedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        RaiseDomainEvent(new PermissionDeprecatedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Unit> Archive(Guid correlationId)
    {
        if (Lifecycle != PermissionLifecycle.Deprecated)
            return Result<Unit>.Failure(AuthorizationErrors.InvalidPermissionLifecycle);

        Lifecycle = PermissionLifecycle.Archived;
        MarkUpdated();
        RaiseDomainEvent(new PermissionArchivedDomainEvent(Id, TenantId, correlationId));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<Permission> CreateNewVersion(string? description, string ownerTeam, Guid correlationId)
    {
        if (Lifecycle != PermissionLifecycle.Published && Lifecycle != PermissionLifecycle.Deprecated)
            return Result<Permission>.Failure(AuthorizationErrors.InvalidVersionSource);

        var newPermission = new Permission(Guid.NewGuid(), TenantId, Key, Action, ResourceType, description ?? Description, ownerTeam);
        newPermission.PermissionVersion = PermissionVersion + 1;
        newPermission.RaiseDomainEvent(new PermissionVersionCreatedDomainEvent(newPermission.Id, TenantId, newPermission.PermissionVersion, correlationId));
        return Result<Permission>.Success(newPermission);
    }

    public Result<Unit> EnsureTenantMatch(Guid expectedTenantId)
    {
        if (TenantId != expectedTenantId)
            return Result<Unit>.Failure(AuthorizationErrors.PermissionTenantMismatch);

        return Result<Unit>.Success(Unit.Value);
    }
}
