using IdentityService.Api.Contracts.Requests;
using IdentityService.Api.Contracts.Responses;
using IdentityService.Api.Extensions;
using IdentityService.Api.Filters;
using IdentityService.Application.Common.Abstractions;
using SharedKernel.Errors;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;
using IdentityService.Application.Features.ActivateUser;
using IdentityService.Application.Features.DeleteUser;
using IdentityService.Application.Features.DisableUser;
using IdentityService.Application.Features.EnableMfa;
using IdentityService.Application.Features.Logout;
using IdentityService.Application.Features.MfaVerify;
using IdentityService.Application.Features.UnlockUser;
using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Api;

namespace IdentityService.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity").WithTags("Identity Sessions").RequireAuthorization();

        group.MapPost("/sessions/logout", LogoutAsync)
            .AddEndpointFilter<ValidationFilter<LogoutRequest>>();
        group.MapDelete("/sessions/{id:guid}", RevokeSessionAsync).RequireAuthorization();

        group.MapPost("/mfa/verify", VerifyMfaAsync)
            .AddEndpointFilter<ValidationFilter<MfaVerifyRequest>>();

        group.MapPost("/mfa/enable", EnableMfaAsync)
            .AddEndpointFilter<ValidationFilter<EnableMfaRequest>>();

        group.MapPost("/users/{userId:guid}/disable", DisableUserAsync);
        group.MapPost("/users/{userId:guid}/activate", ActivateUserAsync);
        group.MapPost("/users/{userId:guid}/unlock", UnlockUserAsync);
        group.MapDelete("/users/{userId:guid}", DeleteUserAsync);

        return app;
    }
    
    private static async ValueTask<IResult> LogoutAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] LogoutRequest? request,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        if (request is null || request.SessionId == Guid.Empty)
        {
            return ResultHttpExtensions.ToHttpResult(
                Result<Unit>.Failure(GeneralErrors.Validation), context.CorrelationId);
        }

        var result = await mediator.Send(new LogoutCommand(request.SessionId), cancellationToken);
        await auditSink.RecordAsync("IdentityService.logout", context.TenantId, context.CorrelationId, null, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(LoggedOut: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> RevokeSessionAsync(
        Guid id,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new LogoutCommand(id), cancellationToken);
        await auditSink.RecordAsync("IdentityService.logout", context.TenantId, context.CorrelationId, null, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(LoggedOut: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> VerifyMfaAsync(
        MfaVerifyRequest request,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new MfaVerifyCommand(request.UserId, request.Code), cancellationToken);
        await auditSink.RecordAsync(result.IsSuccess ? "IdentityService.mfa_verified" : "IdentityService.mfa_failed", context.TenantId, context.CorrelationId, request.UserId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(Verified: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> EnableMfaAsync(
        EnableMfaRequest request,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new EnableMfaCommand(request.UserId, request.Secret), cancellationToken);
        await auditSink.RecordAsync("IdentityService.mfa_enabled", context.TenantId, context.CorrelationId, request.UserId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(Enabled: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> DisableUserAsync(
        Guid userId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? targetTenantId,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new DisableUserCommand(userId, targetTenantId), cancellationToken);
        await auditSink.RecordAsync("identity.user_disabled", context.TenantId, context.CorrelationId, userId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(Disabled: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> ActivateUserAsync(
        Guid userId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? targetTenantId,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new ActivateUserCommand(userId, targetTenantId), cancellationToken);
        await auditSink.RecordAsync("identity.user_activated", context.TenantId, context.CorrelationId, userId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(Activated: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> UnlockUserAsync(
        Guid userId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? targetTenantId,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new UnlockUserCommand(userId, targetTenantId), cancellationToken);
        await auditSink.RecordAsync("identity.user_unlocked", context.TenantId, context.CorrelationId, userId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(Unlocked: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> DeleteUserAsync(
        Guid userId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? targetTenantId,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new DeleteUserCommand(userId, targetTenantId), cancellationToken);
        await auditSink.RecordAsync("identity.user_deleted", context.TenantId, context.CorrelationId, userId, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(new CommandStatusResponse(Deleted: true), context.CorrelationId)
            : result.ToHttpResult(context.CorrelationId);
    }
}