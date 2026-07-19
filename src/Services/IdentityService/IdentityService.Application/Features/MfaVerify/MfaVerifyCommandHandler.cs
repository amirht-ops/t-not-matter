using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.Services;
using MediatR;
using SharedKernel.Errors;
using Unit = SharedKernel.Results.Unit;
using UnitResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.MfaVerify;

public sealed class MfaVerifyCommandHandler(
    IUserRepository users,
    IMfaProvider mfaProvider,
    IMfaSecretProtector mfaSecretProtector,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<MfaVerifyCommand, UnitResult>
{
    public async Task<UnitResult> Handle(MfaVerifyCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;

        var user = await users.GetByIdAsync(context.TenantId, request.UserId, trackChanges: true, cancellationToken);
        if (user is null)
        {
            return UnitResult.Failure(GeneralErrors.NotFound);
        }

        if (!user.MfaSettings.IsEnabled)
        {
            return UnitResult.Failure(GeneralErrors.Unauthorized);
        }

        var mfaSecret = mfaSecretProtector.Unprotect(user.MfaSettings.ProtectedSecret!);
        if (!mfaProvider.VerifyCode(mfaSecret, request.Code, DateTimeOffset.UtcNow))
        {
            return UnitResult.Failure(GeneralErrors.Unauthorized);
        }

        await users.UpdateAsync(user, cancellationToken);
        return UnitResult.Success(Unit.Value);
    }
}
