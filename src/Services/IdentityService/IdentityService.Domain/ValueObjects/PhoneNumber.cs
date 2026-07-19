using IdentityService.Domain.Errors;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace IdentityService.Domain.ValueObjects;

public sealed class PhoneNumber : ValueObject
{
    private PhoneNumber(string value) => Value = value;
    
    public string Value { get; }
    
    public static Result<PhoneNumber> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<PhoneNumber>.Failure(IdentityErrors.PhoneNumberRequired);
        }

        value = value.Trim();

        if (!IsValidE164(value))
        {
            return Result<PhoneNumber>.Failure(IdentityErrors.PhoneNumberInvalid);
        }

        return Result<PhoneNumber>.Success(new PhoneNumber(value));
    }

    private static bool IsValidE164(string value)
    {
        if (value.Length < 10 || value.Length > 15)
            return false;

        if (value.StartsWith('+'))
            return value.Skip(1).All(char.IsDigit);

        return value.All(char.IsDigit);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}