using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Abstractions.Tenant;
using Platform.Infrastructure.Outbox;
using PolicyService.Domain.Aggregates.DebtLedgers;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Aggregates.PrincipalHierarchy;
using PolicyService.Domain.Aggregates.QuotaPolicies;
using PolicyService.Domain.Aggregates.Subscriptions;
using PolicyService.Domain.Aggregates.UsageLedgers;
using SharedKernel.Contract.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Primitives;
using SharedKernel.Infrastructure.Outbox;

namespace PolicyService.Infrastructure.Persistence;

public sealed class PolicyDbContext(DbContextOptions<PolicyDbContext> options, IRequestContextAccessor requestContext) : DbContext(options)
{
    private const string Schema = "policy";

    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<QuotaPolicy> QuotaPolicies => Set<QuotaPolicy>();
    public DbSet<UsageLedger> UsageLedgers => Set<UsageLedger>();
    public DbSet<DebtLedger> DebtLedgers => Set<DebtLedger>();
    public DbSet<PrincipalNode> PrincipalNodes => Set<PrincipalNode>();
    public DbSet<PrincipalEdge> PrincipalEdges => Set<PrincipalEdge>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DeadLetterMessage> DeadLetterMessages => Set<DeadLetterMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PolicyDbContext).Assembly);
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
