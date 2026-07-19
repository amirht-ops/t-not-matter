using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

public sealed class ResourceType : ValueObject
{
    private ResourceType(string value) => Value = value;
    public string Value { get; }
    public static Result<ResourceType> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<ResourceType>.Failure(PolicyErrors.ResourceTypeRequired);
        return Result<ResourceType>.Success(new ResourceType(value.Trim()));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}

public sealed class ResourceId : ValueObject
{
    private ResourceId(string value) => Value = value;
    public string Value { get; }
    public static Result<ResourceId> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<ResourceId>.Failure(PolicyErrors.ResourceIdRequired);
        return Result<ResourceId>.Success(new ResourceId(value.Trim()));
    }
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
