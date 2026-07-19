using PolicyService.Domain.Enums;
using SharedKernel.Domain.Primitives;

namespace PolicyService.Domain.Aggregates.PrincipalHierarchy;

public sealed class PrincipalNode : Entity
{
    private PrincipalNode() { }

    private PrincipalNode(Guid id, Guid tenantId, Guid nodeId, PrincipalKind nodeType)
        : base(id, tenantId)
    {
        NodeId = nodeId;
        NodeType = nodeType;
    }

    public Guid NodeId { get; private set; }

    public PrincipalKind NodeType { get; private set; }

    public static PrincipalNode Create(Guid tenantId, Guid nodeId, PrincipalKind nodeType) =>
        new(Guid.NewGuid(), tenantId, nodeId, nodeType);
}
