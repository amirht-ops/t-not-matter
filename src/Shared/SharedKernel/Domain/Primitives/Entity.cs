using SharedKernel.Domain.Guards;

namespace SharedKernel.Domain.Primitives;

public abstract class Entity
{
    protected Entity() { }

    protected Entity(Guid id, Guid tenantId)
    {
        Id = Guard.NotEmpty(id,nameof(id));
        TenantId = Guard.NotEmpty(tenantId,nameof(tenantId));
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        Version = 1;
    }
    
    public Guid Id { get; private init; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }
    public bool IsDeleted { get; private set; }
    
    protected void MarkUpdated()
    {
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    protected void MoveToTenant(Guid tenantId)
    {
        TenantId = Guard.NotEmpty(tenantId, nameof(tenantId));
        MarkUpdated();
    }

    /// <summary>
    /// Restores persisted timestamps and version. Used by rehydration factories
    /// in the caching layer to reconstruct entities from cached DTOs.
    /// This is NOT a business operation — it's a persistence helper.
    /// </summary>
    protected internal void SetTimestamps(DateTimeOffset createdAt, DateTimeOffset updatedAt, int version)
    {
        // Use reflection-free approach: set via property setters which are accessible to derived classes.
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Version = version;
    }

    protected void SoftDelete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        MarkUpdated();
    }
    
}
