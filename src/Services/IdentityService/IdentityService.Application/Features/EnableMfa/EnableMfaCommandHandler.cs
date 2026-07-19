using IdentityService.Application.Common.Abstractions;
using SharedKernel.Authorization;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.Services;
using MediatR;
using SharedKernel.Errors;
using Unit = SharedKernel.Results.Unit;
using UnitResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.EnableMfa;

public sealed class EnableMfaCommandHandler(
    IUserRepository users,
    IMfaSecretProtector mfaSecretProtector,
    IAuthorizationDecisionService authorizationDecisionService,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<EnableMfaCommand, UnitResult>
{
    public async Task<UnitResult> Handle(EnableMfaCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;

        var user = await users.GetByIdAsync(context.TenantId, request.UserId, trackChanges: true, cancellationToken);
        if (user is null)
        {
            return UnitResult.Failure(GeneralErrors.NotFound);
        }

        // Platform administrators and trusted service principals bypass the per-action OPA
        // check, mirroring the sibling user-management handlers (Activate/Disable/Unlock/
        // Delete). Without this, a platform admin can manage a tenant user's full lifecycle
        // yet cannot enable MFA for that same user — an inconsistent authorization gap.
        if (!context.CanBypassTenantIsolation && !context.IsService)
        {
            var decision = await authorizationDecisionService.DecideAsync(
                new AuthorizationRequest(
                    context.TenantId,
                    context.UserId,
                    IdentityAuthorizationActions.EnableMfa,
                    $"identity.user:{request.UserId}",
                    context.CorrelationId,
                    context.RequestId),
                cancellationToken);
            if (!decision.IsAllowed)
            {
                return UnitResult.Failure(GeneralErrors.Forbidden);
            }
        }


        var enableResult = user.EnableMfa(mfaSecretProtector.Protect(request.Secret), context.CorrelationId);
        if (enableResult.IsFailure)
        {
            return UnitResult.Failure(enableResult.Error);
        }

        await users.UpdateAsync(user, cancellationToken);
        return UnitResult.Success(Unit.Value);
    }
}
