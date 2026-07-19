using IdentityService.Application.Common.Abstractions;
using SharedKernel.Authorization;
using IdentityService.Domain.Repositories;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace IdentityService.Application.Features.Logout;

public sealed class LogoutCommandHandler(
    ISessionRepository sessions,
    IAuthorizationDecisionService authorizationDecisionService,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<LogoutCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;
        var session = await sessions.GetByIdAsync(context.TenantId, request.SessionId, trackChanges: true, cancellationToken);
        if (session is null)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        var decision = await authorizationDecisionService.DecideAsync(
            new AuthorizationRequest(
                context.TenantId,
                context.UserId,
                IdentityAuthorizationActions.Logout,
                $"identity.session:{request.SessionId}",
                context.CorrelationId,
                context.RequestId), cancellationToken);
        if (!decision.IsAllowed)
        {
            return Result<Unit>.Failure(GeneralErrors.Forbidden);
        }

        session.Revoke(context.CorrelationId);
        await sessions.UpdateAsync(session, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}