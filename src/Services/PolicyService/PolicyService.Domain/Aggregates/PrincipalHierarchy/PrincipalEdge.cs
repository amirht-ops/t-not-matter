using SharedKernel.Domain.Primitives;

namespace PolicyService.Domain.Aggregates.PrincipalHierarchy;

public sealed class PrincipalEdge : Entity
{
    private PrincipalEdge() { }

    private PrincipalEdge(Guid id, Guid tenantId, Guid userId, Guid roleId)
        : base(id, tenantId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public static PrincipalEdge Create(Guid tenantId, Guid userId, Guid roleId) =>
        new(Guid.NewGuid(), tenantId, userId, roleId);

    public void Revoke() => SoftDelete();
}
