using System.Security.Cryptography;
using System.Text;
using SharedKernel.Domain.Primitives;
using SharedKernel.Results;

namespace PolicyService.Domain.ValueObjects;

/// <summary>
/// The compiled, immutable Rego module produced by PolicyService (ADR-003). Distributed to OPA;
/// quota/debt thresholds are NEVER encoded here (ADR-018 §15 rejected).
/// </summary>
public sealed class RegoModule : ValueObject
{
    private RegoModule(string source, string hash)
    {
        Source = source;
        Hash = hash;
    }

    public string Source { get; }
    public string Hash { get; }

    public static Result<RegoModule> Create(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Result<RegoModule>.Failure(PolicyErrors.RegoSourceRequired);

        var hash = ComputeHash(source);
        return Result<RegoModule>.Success(new RegoModule(source, hash));
    }

    private static string ComputeHash(string source)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Hash;
    }

    public override string ToString() => Hash;
}
