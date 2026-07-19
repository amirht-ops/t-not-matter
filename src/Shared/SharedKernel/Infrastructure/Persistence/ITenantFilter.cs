namespace SharedKernel.Infrastructure.Persistence;

public interface ITenantFilter
{
    Guid TenantId { get; }
}