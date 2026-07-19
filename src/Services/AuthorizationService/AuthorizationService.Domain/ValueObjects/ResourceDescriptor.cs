using SharedKernel.Domain.Guards;
using SharedKernel.Domain.Primitives;

namespace AuthorizationService.Domain.ValueObjects;

public sealed class ResourceDescriptor : ValueObject
{
    private ResourceDescriptor(string resourceType, string resourceId, Guid? ownerId, IReadOnlyDictionary<string, string> attributes)
    {
        ResourceType = Guard.NotEmpty(resourceType, nameof(resourceType));
        ResourceId = Guard.NotEmpty(resourceId, nameof(resourceId));
        OwnerId = ownerId;
        Attributes = attributes;
    }

    public string ResourceType { get; }
    public string ResourceId { get; }
    public Guid? OwnerId { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }

    public static ResourceDescriptor Create(string resourceType, string resourceId, Guid? ownerId = null, IReadOnlyDictionary<string, string>? attributes = null) =>
        new(resourceType, resourceId, ownerId, attributes ?? new Dictionary<string, string>());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ResourceType.ToUpperInvariant();
        yield return ResourceId;
        yield return OwnerId;
        foreach (var pair in Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            yield return pair.Key;
            yield return pair.Value;
        }
    }
}
