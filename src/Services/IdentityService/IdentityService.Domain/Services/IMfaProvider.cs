namespace IdentityService.Domain.Services;

public interface IMfaProvider
{
    bool VerifyCode(string secret, string code, DateTimeOffset now);
}