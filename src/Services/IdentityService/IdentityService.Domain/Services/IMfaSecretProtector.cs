namespace IdentityService.Domain.Services;

public interface IMfaSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}