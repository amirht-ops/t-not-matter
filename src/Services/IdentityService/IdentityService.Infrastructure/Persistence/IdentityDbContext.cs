using System.Linq.Expressions;
using System.Text.Json;
using Platform.Abstractions.Tenant;
using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Aggregates.User;
using IdentityService.Domain.Aggregates;
using IdentityService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Contract.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Primitives;
using SharedKernel.Infrastructure.Outbox;

namespace IdentityService.Infrastructure.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    IRequestContextAccessor requestContextAccessor) : DbContext(options)
{
    private const string Schema = "Identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AccessRisk> AccessRisks => Set<AccessRisk>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Platform.Infrastructure.Outbox.DeadLetterMessage> DeadLetterMessages => Set<Platform.Infrastructure.Outbox.DeadLetterMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<User>(ConfigureUser);
        modelBuilder.Entity<Session>(ConfigureSession);
        modelBuilder.ApplyConfiguration(new Configurations.AccessRiskConfiguration());
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
        var capturedRoots = CaptureDomainEvents();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        ClearCapturedDomainEvents(capturedRoots);
        return result;
    }

    private void ConfigureUser(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("identity_users");
        builder.HasKey(user => new { user.TenantId, user.Id });
        builder.HasIndex(user => new { user.TenantId, user.Id });
        builder.HasIndex(user => new { user.TenantId, user.PhoneNumber });
        builder.HasIndex(user => new { user.TenantId, user.Username }).IsUnique();

        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.TenantId).ValueGeneratedNever();
        builder.Property(user => user.Version).IsConcurrencyToken();
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(32);
        
        builder.Property(user => user.Username)
            .HasConversion(
                username => username.Value,
                value => Username.Create(value).Value!)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(user => user.PhoneNumber)
            .HasConversion(
                phoneNumber => phoneNumber.Value,
                value => PhoneNumber.Create(value).Value!)
            .HasMaxLength(15)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasConversion(
                email => email!.Value,
                value => Email.Create(value).Value!)
            .HasMaxLength(320)
            .IsRequired(false);

        builder.Property(user => user.PasswordHash)
            .HasConversion(
                passwordHash => passwordHash.Value,
                value => PasswordHash.FromHash(value).Value!)
            .HasMaxLength(512);


        builder.OwnsOne(user => user.MfaSettings, owned =>
        {
            owned.Property(settings => settings.IsEnabled).HasColumnName("MfaEnabled").HasDefaultValue(false);
            owned.Property(settings => settings.ProtectedSecret).HasColumnName("MfaProtectedSecret").HasMaxLength(2048);
        });
        builder.Ignore(user => user.CanAuthenticate);
        builder.Ignore(user => user.DomainEvents);
    }

    private void ConfigureSession(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("identity_sessions");
        builder.HasKey(session => new { session.TenantId, session.Id });
        builder.HasIndex(session => new { session.TenantId, session.Id });
        builder.HasIndex(session => new { session.TenantId, session.RefreshTokenHash });

        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.TenantId).ValueGeneratedNever();
        builder.Property(session => session.UserId);
        builder.Property(session => session.RefreshTokenHash).HasMaxLength(128);
        builder.Property(session => session.PreviousRefreshTokenHash).HasMaxLength(128);
        builder.Property(session => session.RefreshTokenRotatedAt);
        builder.Property(session => session.RefreshTokenReuseDetectedAt);
        builder.Property(session => session.ExpiresAt);
        builder.Property(session => session.RevokedAt);
        builder.Property(session => session.IpAddress).HasMaxLength(64);
        builder.Property(session => session.UserAgent).HasMaxLength(512);
        builder.Property(session => session.Version).IsConcurrencyToken();
        builder.Ignore(session => session.IsActive);
        builder.Ignore(session => session.DomainEvents);
    }

    private static void ConfigureOutboxMessage(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("identity_outbox");
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
        var combined = Expression.AndAlso(tenantBoundary, notDeleted);

        return Expression.Lambda(combined, parameter);
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

    private static void ClearCapturedDomainEvents(AggregateRoot[] roots)
    {
        foreach (var root in roots)
        {
            root.ClearDomainEvents();
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