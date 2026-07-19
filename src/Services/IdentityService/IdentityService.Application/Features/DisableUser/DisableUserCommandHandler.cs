using IdentityService.Application.Common.Abstractions;
using SharedKernel.Authorization;
using IdentityService.Domain.Repositories;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace IdentityService.Application.Features.DisableUser;

public sealed class DisableUserCommandHandler(
    IUserRepository users,
    IAuthorizationDecisionService authorizationDecisionService,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<DisableUserCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DisableUserCommand request, CancellationToken cancellationToken)
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
                    IdentityAuthorizationActions.DisableUser,
                    $"identity.user:{request.UserId}",
                    context.CorrelationId,
                    context.RequestId),
                cancellationToken);
            if (!decision.IsAllowed) return Result<Unit>.Failure(GeneralErrors.Forbidden);
        }

        var disableResult = user.Disable(context.CorrelationId);
        if (disableResult.IsFailure) return Result<Unit>.Failure(disableResult.Error);


        await users.UpdateAsync(user, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}