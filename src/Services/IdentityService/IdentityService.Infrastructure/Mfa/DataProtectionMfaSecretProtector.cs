using IdentityService.Domain.Services;
using Microsoft.AspNetCore.DataProtection;

namespace IdentityService.Infrastructure.Mfa;

public sealed class DataProtectionMfaSecretProtector(IDataProtectionProvider dataProtectionProvider) : IMfaSecretProtector
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("IdentityService.MfaSecret.v1");

    public string Protect(string secret) => protector.Protect(secret);

    public string Unprotect(string protectedSecret) => protector.Unprotect(protectedSecret);
}
