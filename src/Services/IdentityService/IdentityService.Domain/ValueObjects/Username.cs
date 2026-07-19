using System.Security.Principal;
using IdentityService.Domain.Errors;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace IdentityService.Domain.ValueObjects;

public sealed class Username : ValueObject
{
    private Username(string value) => Value = value;
    public string Value { get; }

    public static Result<Username> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Username>.Failure(IdentityErrors.UsernameRequired);
        }

        value = value.Trim();

        if (value.Length < 3 || value.Length > 50)
        {
            return Result<Username>.Failure(IdentityErrors.InvalidUsername);
        }

        return Result<Username>.Success(new Username(value));
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}