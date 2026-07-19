using SharedKernel.Domain.Primitives;
using SharedKernel.Results;
using TenantService.Domain.Errors;

namespace TenantService.Domain.ValueObjects;

public sealed class DepartmentName : ValueObject
{
    private const int MinLength = 1;
    private const int MaxLength = 200;

    private DepartmentName(string value) => Value = value;

    public string Value { get; }

    public static Result<DepartmentName> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<DepartmentName>.Failure(TenantErrors.DepartmentNameRequired);
        }

        var trimmed = value.Trim();

        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            return Result<DepartmentName>.Failure(TenantErrors.DepartmentNameTooLong);
        }

        return Result<DepartmentName>.Success(new DepartmentName(trimmed));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StringComparer.OrdinalIgnoreCase.Equals(Value, Value) ? Value.ToLowerInvariant() : Value;
    }
}