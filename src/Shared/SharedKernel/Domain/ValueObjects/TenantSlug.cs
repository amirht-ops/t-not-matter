using System.Text.RegularExpressions;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;
using SharedKernel.Errors;

namespace SharedKernel.Domain.ValueObjects;

public sealed partial class TenantSlug : ValueObject
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);
    
    private static readonly string[] GlobalReservedSlugs = 
    { 
        "admin", "api", "www", "mail", "smtp", "imap", "pop3", "login", "auth", 
        "signup", "billing", "support", "docs", "status", "app", "demo", 
        "test", "dev", "staging", "prod", "portal", "system", "root", 
        "webmaster", "security", "help", "accounts" 
    };

    private TenantSlug(string value) => Value = value;

    public string Value { get; }

    public static Result<TenantSlug> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<TenantSlug>.Failure(TenantSlugErrors.Required);

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length < 2)
            return Result<TenantSlug>.Failure(TenantSlugErrors.TooShort);

        if (normalized.Length > 63)
            return Result<TenantSlug>.Failure(TenantSlugErrors.TooLong);

        if (normalized.Contains("--"))
            return Result<TenantSlug>.Failure(TenantSlugErrors.InvalidFormat);

        if (!SlugRegex.IsMatch(normalized))
            return Result<TenantSlug>.Failure(TenantSlugErrors.InvalidFormat);

        if (Array.Exists(GlobalReservedSlugs, s => s.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return Result<TenantSlug>.Failure(TenantSlugErrors.IsReserved);

        return Result<TenantSlug>.Success(new TenantSlug(normalized));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
