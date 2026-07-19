using System.Linq.Expressions;
using System.Text.Json;
using AuthorizationService.Domain.Aggregates.Permission;
using AuthorizationService.Domain.Aggregates.PermissionGrant;
using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.Aggregates.RoleAssignment;
using AuthorizationService.Domain.Aggregates.UsageTracking;
using Microsoft.EntityFrameworkCore;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Primitives;
using SharedKernel.Infrastructure.Outbox;

namespace AuthorizationService.Infrastructure.Persistence;

public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options, IRequestContextAccessor requestContext) : DbContext(options)
{
    private const string Schema = "authz";

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    public DbSet<UsageTracking> UsageTrackings => Set<UsageTracking>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Platform.Infrastructure.Outbox.DeadLetterMessage> DeadLetterMessages => Set<Platform.Infrastructure.Outbox.DeadLetterMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthorizationDbContext).Assembly);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType)))
        {
            entityType.SetQueryFilter(CreateTenantSoftDeleteFilter(entityType.ClrType));
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var rootsWithEvents = ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToArray();

        foreach (var root in rootsWithEvents)
        {
            foreach (var domainEvent in root.DomainEvents)
            {
                OutboxMessages.Add(ToOutboxMessage(domainEvent));
            }
        }

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        foreach (var root in rootsWithEvents)
        {
            root.ClearDomainEvents();
        }

        return result;
    }

    private LambdaExpression CreateTenantSoftDeleteFilter(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "entity");
        var tenantId = Expression.Property(parameter, nameof(Entity.TenantId));
        var isDeleted = Expression.Property(parameter, nameof(Entity.IsDeleted));
        var currentContext = Expression.Property(Expression.Constant(this), nameof(CurrentRequestContext));
        var currentTenantId = Expression.Property(currentContext, nameof(RequestContext.TenantId));
        var canBypass = Expression.Property(currentContext, nameof(RequestContext.CanBypassTenantIsolation));
        var notDeleted = Expression.Equal(isDeleted, Expression.Constant(false));
        var tenantBoundary = Expression.OrElse(
            canBypass,
            Expression.Equal(tenantId, currentTenantId));
        var combined = Expression.AndAlso(tenantBoundary, notDeleted);

        return Expression.Lambda(combined, parameter);
    }

    private RequestContext CurrentRequestContext => requestContext.Context;

    internal static OutboxMessage ToOutboxMessage(IDomainEvent domainEvent)
    {
        var payloadElement = JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType());
        var payloadJson = payloadElement.GetRawText();
        var envelope = new EventEnvelope(domainEvent.EventId, domainEvent.TenantId, domainEvent.CorrelationId, domainEvent.EventTypeName, domainEvent.Version, domainEvent.OccurredAt, payloadElement, domainEvent.CausationId);
        return new OutboxMessage { Id = envelope.EventId, TenantId = envelope.TenantId, CorrelationId = envelope.CorrelationId, CausationId = envelope.CausationId, EventType = envelope.EventType, Payload = payloadJson, Version = envelope.Version, CreatedAt = envelope.OccurredAt };
    }
}
