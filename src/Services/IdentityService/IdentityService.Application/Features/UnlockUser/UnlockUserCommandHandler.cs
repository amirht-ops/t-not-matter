using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Repositories;
using MediatR;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace IdentityService.Application.Features.UnlockUser;

public sealed class UnlockUserCommandHandler(
    IUserRepository users,
    IAuthorizationDecisionService authorizationDecisionService,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<UnlockUserCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;
        var targetTenantId = request.TargetTenantId ?? context.TenantId;

        var user = await users.GetByIdAsync(targetTenantId, request.UserId, trackChanges: true, cancellationToken);
        if (user is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        if (!context.CanBypassTenantIsolation && !context.IsService)
        {
            var decision = await authorizationDecisionService.DecideAsync(
                new AuthorizationRequest(
                    context.TenantId,
                    context.UserId,
                    IdentityAuthorizationActions.UnlockUser,
                    $"identity.user:{request.UserId}",
                    context.CorrelationId,
                    context.RequestId),
                cancellationToken);
            if (!decision.IsAllowed)
            {
                return Result<Unit>.Failure(GeneralErrors.Forbidden);
            }
        }
        var unlockResult = user.Unlock(context.CorrelationId);
        if (unlockResult.IsFailure)
        {
            return Result<Unit>.Failure(unlockResult.Error);
        }

        await users.UpdateAsync(user, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}