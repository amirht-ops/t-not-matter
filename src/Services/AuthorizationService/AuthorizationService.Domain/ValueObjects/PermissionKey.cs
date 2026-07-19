using System.Text.RegularExpressions;
using AuthorizationService.Domain.Errors;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace AuthorizationService.Domain.ValueObjects;

public sealed partial class PermissionKey : ValueObject
{
    private static readonly Regex Pattern = PermissionKeyPattern();

    private PermissionKey(string value)
    {
        Value = value;
    }

    public string Value { get; }
    public bool IsWildcard => Value.EndsWith(".*", StringComparison.Ordinal);

    public bool Matches(string permissionKey)
    {
        if (!IsWildcard) return string.Equals(Value, permissionKey, StringComparison.OrdinalIgnoreCase);
        var resourcePrefix = Value[..^2];
        return permissionKey.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase) && permissionKey.Contains('.');
    }

    public static Result<PermissionKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<PermissionKey>.Failure(AuthorizationErrors.PermissionKeyInvalidFormat);

        if (!Pattern.IsMatch(value))
            return Result<PermissionKey>.Failure(AuthorizationErrors.PermissionKeyInvalidFormat);

        if (!value.Contains('.') || value.StartsWith('.') || value.EndsWith('.'))
            return Result<PermissionKey>.Failure(AuthorizationErrors.GlobalPermissionProhibited);

        return Result<PermissionKey>.Success(new PermissionKey(value));
    }

    /// <summary>For EF Core materialization only. Use <see cref="Create"/> for domain-driven creation.</summary>
    public static PermissionKey FromDb(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value.ToUpperInvariant(); }
    public override string ToString() => Value;

    [GeneratedRegex(@"^[A-Z][a-zA-Z0-9]*\.(\*|[A-Z][a-zA-Z0-9]*)$", RegexOptions.Compiled)]
    private static partial Regex PermissionKeyPattern();
}
