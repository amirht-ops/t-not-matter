using IdentityService.Domain.Errors;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace IdentityService.Domain.ValueObjects;

public sealed class PasswordHash : ValueObject
{
    private PasswordHash(string value) => Value = value;

    public string Value { get; }

    public static Result<PasswordHash> FromHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<PasswordHash>.Failure(IdentityErrors.PasswordHashRequired);
        }

        return Result<PasswordHash>.Success(new PasswordHash(value));
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}