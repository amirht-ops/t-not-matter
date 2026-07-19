using IdentityService.Application.Common.Abstractions;
using SharedKernel.Authorization;
using IdentityService.Domain.Repositories;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace IdentityService.Application.Features.ActivateUser;

public sealed class ActivateUserCommandHandler(
    IUserRepository users,
    IAuthorizationDecisionService authorizationDecisionService,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<ActivateUserCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;
        var targetTenantId = request.TargetTenantId ?? context.TenantId;

        var user = await users.GetByIdAsync(targetTenantId, request.UserId, trackChanges: true, cancellationToken);
        if (user is null) return Result<Unit>.Failure(GeneralErrors.NotFound);

        if (!context.CanBypassTenantIsolation && !context.IsService)
        {
            var decision = await authorizationDecisionService.DecideAsync(
                new AuthorizationRequest(
                    context.TenantId,
                    context.UserId,
                    IdentityAuthorizationActions.ActivateUser,
                    $"identity.user:{request.UserId}",
                    context.CorrelationId,
                    context.RequestId),
                cancellationToken);
            if (!decision.IsAllowed) return Result<Unit>.Failure(GeneralErrors.Forbidden);
        }
        
        var activateResult = user.Activate(context.CorrelationId);
        if (activateResult.IsFailure) return Result<Unit>.Failure(activateResult.Error);
        
        await users.UpdateAsync(user, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}