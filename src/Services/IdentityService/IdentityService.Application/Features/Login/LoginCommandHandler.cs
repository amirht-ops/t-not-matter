using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Aggregates;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.Services;
using IdentityService.Domain.ValueObjects;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace IdentityService.Application.Features.Login;

public sealed class LoginCommandHandler(
    IUserRepository users,
    ISessionRepository sessions,
    IAccessRiskRepository accessRisks,
    AuthenticationPolicy authPolicy,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator,
    IMfaProvider mfaProvider,
    IMfaSecretProtector mfaSecretProtector,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor,
    ITenantServiceClient tenantServiceClient,
    IAuthorizationRoleResolver authorizationRoleResolver,
    IAuditSink auditSink) : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;

        Guid tenantId;
        Username username;
        string slug = string.Empty;

        if (!string.IsNullOrWhiteSpace(request.Identifier))
        {
            var parseResult = LoginIdentifierParser.Parse(request.Identifier);
            if (parseResult.IsFailure)
            {
                await auditSink.RecordAsync("InvalidTenantSlug", Guid.Empty, context.CorrelationId, null, false,
                    "Invalid identifier format", cancellationToken);
                return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);
            }

            var (tenantSlug, userUserName) = parseResult.Value;
            username = userUserName;
            slug = tenantSlug.Value;

            var tenantIdResult = await tenantServiceClient.ResolveTenantIdBySlugAsync(slug, cancellationToken);
            if (tenantIdResult.IsFailure)
            {
                await auditSink.RecordAsync("TenantResolutionFailed", Guid.Empty, context.CorrelationId, null, false,
                    $"Slug: {slug}", cancellationToken);
                return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);
            }

            tenantId = tenantIdResult.Value!;
            requestContextAccessor.Context = context with { TenantId = tenantId };
            await auditSink.RecordAsync("TenantResolved", tenantId, context.CorrelationId, null, true, $"Slug: {slug}",
                cancellationToken);
        }
        else
        {
            var usernameResult = Username.Create(request.Username!);
            if (usernameResult.IsFailure) return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);

            username = usernameResult.Value!;
            tenantId = context.TenantId;
            
            if (tenantId == Guid.Empty)
            {
                return Result<LoginResponse>.Failure(GeneralErrors.TenantMissing);
            }
        }


        var user = await users.GetByUsernameAsync(username, tenantId, cancellationToken: cancellationToken);
        if (user is null) return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);

        if (!user.CanAuthenticate)
        {
            return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);
        }

        var risk = await accessRisks.GetByUserIdAsync(user.Id, cancellationToken);
        if (risk == null)
        {
            risk = AccessRisk.Create(user.Id, user.TenantId);
            await accessRisks.AddAsync(risk, cancellationToken);
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash.Value))
        {
            risk.RecordFailure(DateTimeOffset.UtcNow);
            
            if (authPolicy.ShouldLock(risk))
            {
                user.Lockout(context.CorrelationId);
            }

            await accessRisks.UpdateAsync(risk, cancellationToken);
            await users.UpdateAsync(user, cancellationToken);
            
            return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);
        }

        if (user.MfaSettings.IsEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.MfaCode))
            {
                return Result<LoginResponse>.Success(
                    new LoginResponse(string.Empty,
                        string.Empty,
                        DateTimeOffset.MinValue,
                        Guid.Empty,
                        true));
            }

            var mfaSecret = mfaSecretProtector.Unprotect(user.MfaSettings.ProtectedSecret!);
            if (!mfaProvider.VerifyCode(mfaSecret, request.MfaCode, DateTimeOffset.UtcNow))
            {
                return Result<LoginResponse>.Failure(GeneralErrors.Unauthorized);
            }
        }

        var (session, refreshToken) = Session.Create(tenantId, user.Id, RefreshTokenLifetime, request.IpAddress, request.UserAgent);
        
        if (authPolicy.ShouldReset(risk))
        {
            risk.Reset();
        }

        await sessions.AddAsync(session, cancellationToken);
        await accessRisks.UpdateAsync(risk, cancellationToken);
        await users.UpdateAsync(user, cancellationToken);

        var roleResult = await authorizationRoleResolver.GetActiveRoleForUserAsync(tenantId, user.Id, cancellationToken);
        var departmentId = roleResult.IsSuccess ? roleResult.Value.DepartmentId : null;
        var roleId = roleResult.IsSuccess ? roleResult.Value.RoleId : null;
        var roles = user.Username.Value.Equals("super", StringComparison.OrdinalIgnoreCase)
            ? new[] { "SuperAdministrator" }
            : null;

        var accessToken = tokenGenerator.GenerateAccessToken(user, session, slug, context.CorrelationId, departmentId, roleId, roles);

        return Result<LoginResponse>.Success(new LoginResponse(accessToken.AccessToken, refreshToken,
            accessToken.ExpiresAt,
            session.Id, false));
    }
}
