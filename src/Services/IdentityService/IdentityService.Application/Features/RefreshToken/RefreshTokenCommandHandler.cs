using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.Services;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace IdentityService.Application.Features.RefreshToken;

public sealed class RefreshTokenCommandHandler(
    IUserRepository users,
    ISessionRepository sessions,
    ITokenGenerator tokenGenerator,
    IAuditSink auditSink,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor) : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    public async Task<Result<RefreshTokenResponse>> Handle(RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;

        if (!IdentityService.Domain.Aggregates.Session.RefreshToken.TryParse(request.RefreshToken, out var tokenTenantId, out _, out _))
        {
            return Result<RefreshTokenResponse>.Failure(GeneralErrors.Unauthorized);
        }

        requestContextAccessor.Context = context with { TenantId = tokenTenantId };

        var session = await sessions.GetByRefreshTokenAsync(request.RefreshToken, trackChanges: true, cancellationToken);
        if (session is null || !session.IsActive)
        {
            var replayedSession =
                await sessions.GetByPreviousRefreshTokenAsync(request.RefreshToken, trackChanges: true,
                    cancellationToken);

            if (replayedSession is not null && replayedSession.MatchesPreviousRefreshToken(request.RefreshToken))
            {
                replayedSession.MarkRefreshTokenReuseDetected(context.CorrelationId);
                await auditSink.RecordAsync(
                    "security.refresh_token_reuse_detected",
                    replayedSession.TenantId,
                    context.CorrelationId,
                    replayedSession.UserId,
                    false,
                    $"SessionId={replayedSession.Id}",
                    cancellationToken);
            }

            return Result<RefreshTokenResponse>.Failure(GeneralErrors.Unauthorized);
        }

        requestContextAccessor.Context = context with { TenantId = session.TenantId, UserId = session.UserId };

        var user = await users.GetByIdAsync(session.TenantId, session.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            return Result<RefreshTokenResponse>.Failure(GeneralErrors.Unauthorized);
        }
        if (!user.CanAuthenticate)
        {
            return Result<RefreshTokenResponse>.Failure(GeneralErrors.Unauthorized);
        }

        var newRefreshToken = session.RotateRefreshToken(RefreshTokenLifetime, context.CorrelationId);
        await sessions.UpdateAsync(session, cancellationToken);
        var accessToken = tokenGenerator.GenerateAccessToken(user, session, string.Empty, context.CorrelationId);
        return Result<RefreshTokenResponse>.Success(new RefreshTokenResponse(accessToken.AccessToken,
            newRefreshToken, accessToken.ExpiresAt, session.Id));
    }
}
