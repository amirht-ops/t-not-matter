using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Primitives;
using SharedKernel.Infrastructure.Outbox;
using TenantService.Domain.Aggregates;
using TenantService.Domain.ValueObjects;

namespace TenantService.Infrastructure.Persistence;

public sealed class TenantDbContext(
    DbContextOptions<TenantDbContext> options,
    IRequestContextAccessor requestContextAccessor)
    : DbContext(options)
{
    private const string Schema = "Tenant";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Platform.Infrastructure.Outbox.DeadLetterMessage> DeadLetterMessages => Set<Platform.Infrastructure.Outbox.DeadLetterMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Tenant>(ConfigureTenant);
        modelBuilder.Entity<Department>(ConfigureDepartment);
        modelBuilder.Entity<OutboxMessage>(ConfigureOutboxMessage);
        modelBuilder.ApplyConfiguration(new Configurations.DeadLetterMessageConfiguration());

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType)))
        {
            entityType.SetQueryFilter(CreateTenantSoftDeleteFilter(entityType.ClrType));
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var affectedRoots = CaptureDomainEvents();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        ClearCapturedDomainEvents(affectedRoots);
        return result;
    }

    private void ConfigureTenant(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(tenant => new { tenant.TenantId, tenant.Id });
        builder.HasIndex(tenant => new { tenant.TenantId, tenant.Id });
        
        builder.HasIndex("TenantId", "Identifier")
            .IsUnique()
            .HasDatabaseName("ix_tenants_tenantid_identifier");

        builder.Property(tenant => tenant.Id).ValueGeneratedNever();
        builder.Property(tenant => tenant.TenantId).ValueGeneratedNever();
        builder.Property(tenant => tenant.Version).IsConcurrencyToken();
        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(tenant => tenant.PlanTier).HasConversion<string>().HasMaxLength(32);
        builder.Property(tenant => tenant.CreatedAt);
        builder.Property(tenant => tenant.UpdatedAt);
        builder.Property(tenant => tenant.Name).HasMaxLength(200);

        builder.Property(tenant => tenant.Code)
            .HasConversion(code => code.Value, value => TenantCode.FromString(value).Value!)
            .HasMaxLength(30)
            .HasColumnName("Identifier");

        builder.Property(tenant => tenant.Slug)
            .HasConversion(slug => slug.Value, value => SharedKernel.Domain.ValueObjects.TenantSlug.Create(value).Value!)
            .HasMaxLength(63)
            .HasColumnName("Slug");

        builder.HasIndex("Slug")
            .IsUnique()
            .HasDatabaseName("ix_tenants_slug");

        builder.Ignore(tenant => tenant.Identifier);
        builder.Ignore(tenant => tenant.IsActive);
        builder.Ignore(tenant => tenant.IsDisabled);
        builder.Ignore(tenant => tenant.IsSuspended);
        builder.Ignore(tenant => tenant.DomainEvents);
    }

    private static void ConfigureDepartment(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("tenant_departments");
        builder.HasKey(department => new { department.TenantId, department.Id });
        builder.HasIndex(department => new { department.TenantId, department.Id });
        builder.HasIndex("TenantId", "Name")
            .IsUnique()
            .HasDatabaseName("ix_departments_tenantid_name");

        builder.Property(department => department.Id).ValueGeneratedNever();
        builder.Property(department => department.TenantId).ValueGeneratedNever();
        builder.Property(department => department.Version).IsConcurrencyToken();
        builder.Property(department => department.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(department => department.CreatedAt);
        builder.Property(department => department.UpdatedAt);
        builder.Property(department => department.Description).HasMaxLength(1000);

        builder.Property(department => department.Name)
            .HasConversion(name => name.Value, value => DepartmentName.Create(value).Value!)
            .HasMaxLength(200)
            .HasColumnName("Name");

        builder.Ignore(department => department.IsActive);
        builder.Ignore(department => department.DomainEvents);
    }

    private static void ConfigureOutboxMessage(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("tenant_outbox");
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => new { message.TenantId, message.ProcessedAt, message.NextRetryAt });
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.CausationId);
        builder.Property(message => message.EventType).HasMaxLength(256);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.Property(message => message.Error).HasMaxLength(2048);
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
        return Expression.Lambda(Expression.AndAlso(tenantBoundary, notDeleted), parameter);
    }

    private RequestContext CurrentRequestContext => requestContextAccessor.Context;

    private AggregateRoot[] CaptureDomainEvents()
    {
        var aggregateRoots = ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        foreach (var aggregateRoot in aggregateRoots)
        {
            foreach (var domainEvent in aggregateRoot.DomainEvents)
            {
                OutboxMessages.Add(ToOutboxMessage(domainEvent));
            }
        }

        return aggregateRoots;
    }

    private static void ClearCapturedDomainEvents(AggregateRoot[] aggregateRoots)
    {
        foreach (var aggregateRoot in aggregateRoots)
        {
            aggregateRoot.ClearDomainEvents();
        }
    }

    private static OutboxMessage ToOutboxMessage(IDomainEvent domainEvent)
    {
        var envelope = new EventEnvelope(
            domainEvent.EventId,
            domainEvent.TenantId,
            domainEvent.CorrelationId,
            domainEvent.EventTypeName,
            domainEvent.Version,
            domainEvent.OccurredAt,
            JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType()),
            domainEvent.CausationId);

        return new OutboxMessage
        {
            Id = envelope.EventId,
            TenantId = envelope.TenantId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            EventType = envelope.EventType,
            Payload = envelope.Payload.GetRawText(),
            Version = envelope.Version,
            CreatedAt = envelope.OccurredAt
        };
    }
}
