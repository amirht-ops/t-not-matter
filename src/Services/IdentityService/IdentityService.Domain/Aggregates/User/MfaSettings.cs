using IdentityService.Domain.Errors;
using SharedKernel.Results;

namespace IdentityService.Domain.Aggregates.User;

public sealed record MfaSettings(bool IsEnabled, string? ProtectedSecret)
{
    public static readonly MfaSettings Disabled = new(false, null);

    public static Result<MfaSettings> Enable(string protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return Result<MfaSettings>.Failure(IdentityErrors.MfaSecretRequired);
        }

        return Result<MfaSettings>.Success(new MfaSettings(true, protectedSecret.Trim()));
    }
}
