using IdentityService.Application.Common.Abstractions;
using SharedKernel.Authorization;
using IdentityService.Domain.Repositories;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace IdentityService.Application.Features.DeleteUser;

public sealed class DeleteUserCommandHandler(
    IUserRepository users,
    IAuthorizationDecisionService authorizationDecisionService,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<DeleteUserCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
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
                    IdentityAuthorizationActions.DeleteUser,
                    $"identity.user:{request.UserId}",
                    context.CorrelationId,
                    context.RequestId),
                cancellationToken);
            if (!decision.IsAllowed) return Result<Unit>.Failure(GeneralErrors.Forbidden);
        }

        var deleteResult = user.Delete(context.CorrelationId);
        if (deleteResult.IsFailure) return Result<Unit>.Failure(deleteResult.Error);
        
        await users.UpdateAsync(user, cancellationToken);
        
        return Result<Unit>.Success(Unit.Value);
    }
}