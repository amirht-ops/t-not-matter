using IdentityService.Api.Contracts.Requests;
using IdentityService.Api.Contracts.Responses;
using IdentityService.Api.Extensions;
using IdentityService.Api.Filters;
using IdentityService.Application.Common.Abstractions;
using IdentityService.Application.Features.Login;
using IdentityService.Application.Features.RefreshToken;
using IdentityService.Application.Features.Register;
using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Contract.Api;
using SharedKernel.Responses;

namespace IdentityService.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity/auth").WithTags("Identity Auth");

        group.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationFilter<RefreshTokenRequest>>()
            .AllowAnonymous();
        

        return app;
    }

    private static async ValueTask<IResult> RegisterAsync(
        RegisterRequest request,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new RegisterCommand(request.PhoneNumber, request.UserName, request.Password, request.Email),
            cancellationToken);
        await auditSink.RecordAsync("IdentityService.user_registered", context.TenantId, context.CorrelationId,
            result.Value?.UserId, result.IsSuccess, result.IsFailure ? result.Error.Description : null,
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/identity/users/{result.Value!.UserId}",
                ApiResponse<RegisterResponse>.Ok(result.Value!, context.CorrelationId))
            : result.ToHttpResult(context.CorrelationId);
    }

    private static async ValueTask<IResult> LoginAsync(
        LoginRequest request,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new LoginCommand(request.Identifier,request.UserName, request.Password, request.MfaCode, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.FirstOrDefault()), cancellationToken);
        var auditEvent = result.IsSuccess && result.Value!.MfaRequired
            ? "IdentityService.mfa_required"
            : result.IsSuccess ? "IdentityService.login_succeeded" : "IdentityService.login_failed";
        await auditSink.RecordAsync(auditEvent, context.TenantId, context.CorrelationId, null, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToHttpResult(context.CorrelationId);
        }

        var response = result.Value!;
        var token = new TokenResponse(response.AccessToken, response.RefreshToken, response.AccessTokenExpiresAt, response.SessionId, response.MfaRequired);
        return ResultHttpExtensions.ToOkHttpResult(token, context.CorrelationId);
    }

    private static async ValueTask<IResult> RefreshAsync(
        RefreshTokenRequest request,
        IMediator mediator,
        IAuditSink auditSink,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
        await auditSink.RecordAsync(result.IsSuccess ? "IdentityService.refresh_succeeded" : "IdentityService.refresh_failed", context.TenantId, context.CorrelationId, null, result.IsSuccess, result.IsFailure ? result.Error.Description : null, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToHttpResult(context.CorrelationId);
        }

        var response = result.Value!;
        var token = new TokenResponse(response.AccessToken, response.RefreshToken, response.AccessTokenExpiresAt, response.SessionId, false);
        return ResultHttpExtensions.ToOkHttpResult(token, context.CorrelationId);
    }
}
