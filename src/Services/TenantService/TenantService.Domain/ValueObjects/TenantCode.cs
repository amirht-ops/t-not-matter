using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;
using TenantService.Domain.Errors;

namespace TenantService.Domain.ValueObjects;

public sealed partial class TenantCode : ValueObject
{
    private const string Prefix = "tnnt_";
    private static readonly Regex IdentifierRegex = new(@"^tnnt_[a-z0-9]{22}$", RegexOptions.Compiled);

    private TenantCode(string value) => Value = value;

    public string Value { get; }

    public static Result<TenantCode> CreateNew()
    {
        var randomPart = GenerateRandomPart();
        var value = $"{Prefix}{randomPart}";
        return Result<TenantCode>.Success(new TenantCode(value));
    }

    public static Result<TenantCode> FromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<TenantCode>.Failure(TenantErrors.InvalidIdentifierFormat);
        }

        var trimmed = value.Trim();

        if (!IdentifierRegex.IsMatch(trimmed))
        {
            return Result<TenantCode>.Failure(TenantErrors.InvalidIdentifierFormat);
        }

        return Result<TenantCode>.Success(new TenantCode(trimmed));
    }

    private static string GenerateRandomPart()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> result = stackalloc char[22];
        for (var i = 0; i < 22; i++)
            result[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(result);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(TenantCode tenantCode) => tenantCode.Value;
}