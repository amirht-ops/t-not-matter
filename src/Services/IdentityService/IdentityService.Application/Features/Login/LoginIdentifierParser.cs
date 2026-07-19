using IdentityService.Domain.ValueObjects;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace IdentityService.Application.Features.Login;

public static class LoginIdentifierParser
{
    public static Result<(TenantSlug slug, Username username)> Parse(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return Result<(TenantSlug slug, Username username)>.Failure(TenantSlugErrors.Required);

        var firstDotIndex = identifier.IndexOf('.');
        if (firstDotIndex <= 0 || firstDotIndex == identifier.Length - 1)
            return Result<(TenantSlug slug, Username username)>.Failure(TenantSlugErrors.InvalidFormat);

        var slugPart = identifier[..firstDotIndex];
        var usernamePart = identifier[(firstDotIndex + 1)..];

        var slugResult = TenantSlug.Create(slugPart);
        if (slugResult.IsFailure)
            return Result<(TenantSlug slug, Username username)>.Failure(slugResult.Error);

        var usernameResult = Username.Create(usernamePart);
        if (usernameResult.IsFailure)
            return Result<(TenantSlug slug, Username username)>.Failure(usernameResult.Error);

        return Result<(TenantSlug Slug, Username username)>.Success((slugResult.Value!, usernameResult.Value!));
    }
}
