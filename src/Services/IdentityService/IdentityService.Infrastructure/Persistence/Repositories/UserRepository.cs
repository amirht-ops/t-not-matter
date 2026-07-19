using IdentityService.Domain.Aggregates.User;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.ValueObjects;
using IdentityService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SharedKernel.Contract.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Infrastructure.Outbox;

namespace IdentityService.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(IdentityDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid tenantId, Guid id, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(user => user.TenantId == tenantId && user.Id == id, cancellationToken);
    }

    public Task<User?> GetByIdAcrossTenantsAsync(Guid id, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.IgnoreQueryFilters();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(Username username, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(user => user.TenantId == tenantId && user.Username == username, cancellationToken);
    }

    public Task<User?> GetByUsernameUnSafeAsync(Username username, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.IgnoreQueryFilters().FirstOrDefaultAsync(user => user.TenantId == tenantId && user.Username == username, cancellationToken);
    }

    public Task<bool> ExistsByUsernameAsync(Username username, Guid tenantId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.TenantId == tenantId && user.Username == username, cancellationToken);

    public Task<User?> GetByEmailAsync(Email email, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(user => user.TenantId == tenantId && user.Email == email, cancellationToken);
    }

    public Task<bool> ExistsByEmailAsync(Email email, Guid tenantId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.TenantId == tenantId && user.Email == email, cancellationToken);

    public Task<User?> GetByPhoneNumberAsync(PhoneNumber phoneNumber, Guid tenantId, bool trackChanges = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsQueryable();
        if (!trackChanges) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(user => user.TenantId == tenantId && user.PhoneNumber == phoneNumber, cancellationToken);
    }

    public Task<bool> ExistsByPhoneNumberAsync(PhoneNumber phoneNumber, Guid tenantId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.TenantId == tenantId && user.PhoneNumber == phoneNumber, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await dbContext.Users.AddAsync(user, cancellationToken);
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(user).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            dbContext.Users.Update(user);
        }
        return Task.CompletedTask;
    }

    public async Task MoveToTenantAsync(User user, Guid oldTenantId, CancellationToken cancellationToken = default)
    {
        var entry = dbContext.Entry(user);
        if (entry.State == EntityState.Detached)
            dbContext.Users.Attach(user);

        var domainEvents = user.DomainEvents.ToArray();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Identity".identity_users
            SET "TenantId" = {user.TenantId}, "UpdatedAt" = {user.UpdatedAt}, "Version" = {user.Version}
            WHERE "TenantId" = {oldTenantId} AND "Id" = {user.Id}
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Identity".identity_access_risks
            SET "TenantId" = {user.TenantId}, "UpdatedAt" = {user.UpdatedAt}, "Version" = "Version" + 1
            WHERE "TenantId" = {oldTenantId} AND "Id" = {user.Id}
            """,
            cancellationToken);

        foreach (var domainEvent in domainEvents)
            dbContext.OutboxMessages.Add(ToOutboxMessage(domainEvent));

        user.ClearDomainEvents();
    }

    private static OutboxMessage ToOutboxMessage(IDomainEvent domainEvent)
    {
        var payloadElement = JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType());
        var envelope = new EventEnvelope(
            domainEvent.EventId,
            domainEvent.TenantId,
            domainEvent.CorrelationId,
            domainEvent.EventTypeName,
            domainEvent.Version,
            domainEvent.OccurredAt,
            payloadElement,
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
