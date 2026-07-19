using AuthorizationService.Api.Contracts.Requests;
using AuthorizationService.Api.Contracts.Responses;
using AuthorizationService.Api.Extensions;
using AuthorizationService.Application.Features.EvaluateAuthorizationDecision;
using AuthorizationService.Application.Features.EvaluateBatchAuthorizationDecisions;
using AuthorizationService.Application.Features.GetEffectivePermissions;
using AuthorizationService.Application.Features.RecordOperationResult;
using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using SharedKernel.Responses;

namespace AuthorizationService.Api.Endpoints;

public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/authorization/decisions")
            .WithTags("Authorization Decisions")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);
        group.MapPost("/evaluate", EvaluateAsync);
        group.MapPost("/batch", BatchAsync);
        group.MapGet("/subjects/{subjectId:guid}/effective-permissions", GetEffectivePermissionsAsync);
        group.MapPost("/record-result", RecordOperationResultAsync);
        return app;
    }

    private static async ValueTask<IResult> EvaluateAsync(EvaluateAuthorizationRequest request, IMediator mediator, IRequestContextAccessor accessor, HttpContext http, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var ipAddress = http.Connection.RemoteIpAddress?.ToString();
        var userAgent = http.Request.Headers.UserAgent.FirstOrDefault();
        var result = await mediator.Send(new EvaluateAuthorizationDecisionCommand(request.SubjectId, request.Action, request.ResourceType, request.ResourceId, request.OwnerId, request.ResourceAttributes ?? new Dictionary<string, string>(), request.EnvironmentAttributes ?? new Dictionary<string, string>(), request.UsageAttributes ?? new Dictionary<string, string>(), ipAddress, userAgent), cancellationToken);
        if (result.IsFailure) return result.ToHttpResult(context.CorrelationId);
        var value = result.Value!;
        return TypedResults.Ok(ApiResponse<AuthorizationDecisionResponse>.Ok(new AuthorizationDecisionResponse(value.IsAllowed, value.Decision, value.ReasonCode, value.ReasonMessage, value.PolicyId, value.PolicyVersion, value.EvaluatedAtUtc, value.EvaluationDurationMs, value.CorrelationId, value.RequestId), context.CorrelationId));
    }

    private static async ValueTask<IResult> BatchAsync(BatchEvaluateAuthorizationRequest request, IMediator mediator, IRequestContextAccessor accessor, HttpContext http, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var ipAddress = http.Connection.RemoteIpAddress?.ToString();
        var userAgent = http.Request.Headers.UserAgent.FirstOrDefault();
        var commands = request.Decisions.Select(item => new EvaluateAuthorizationDecisionCommand(item.SubjectId, item.Action, item.ResourceType, item.ResourceId, item.OwnerId, item.ResourceAttributes ?? new Dictionary<string, string>(), item.EnvironmentAttributes ?? new Dictionary<string, string>(), item.UsageAttributes ?? new Dictionary<string, string>(), ipAddress, userAgent)).ToArray();
        var result = await mediator.Send(new EvaluateBatchAuthorizationDecisionsCommand(commands, ipAddress, userAgent), cancellationToken);
        if (result.IsFailure) return result.ToHttpResult(context.CorrelationId);
        var decisions = result.Value!.Decisions.Select(value => new AuthorizationDecisionResponse(value.IsAllowed, value.Decision, value.ReasonCode, value.ReasonMessage, value.PolicyId, value.PolicyVersion, value.EvaluatedAtUtc, value.EvaluationDurationMs, value.CorrelationId, value.RequestId)).ToArray();
        return TypedResults.Ok(ApiResponse<BatchAuthorizationDecisionResponse>.Ok(new BatchAuthorizationDecisionResponse(decisions), context.CorrelationId));
    }

    private static async ValueTask<IResult> GetEffectivePermissionsAsync(Guid subjectId, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var result = await mediator.Send(new GetEffectivePermissionsQuery(subjectId), cancellationToken);
        if (result.IsFailure) return result.ToHttpResult(context.CorrelationId);
        return TypedResults.Ok(ApiResponse<EffectivePermissionsResponse>.Ok(new EffectivePermissionsResponse(result.Value!.SubjectId, result.Value.Permissions), context.CorrelationId));
    }

    private static async ValueTask<IResult> RecordOperationResultAsync(RecordOperationResultRequest request, IMediator mediator, IRequestContextAccessor accessor, CancellationToken cancellationToken)
    {
        var context = accessor.Context;
        var cmd = new RecordOperationResultCommand(
            request.RequestId, context.CorrelationId, context.TenantId, request.SubjectId,
            request.Action, request.ResourceType, request.ResourceId, request.ResourceCount,
            request.UsagePolicyKey, request.UsageLimit)
        {
            DailyLimit = request.DailyLimit,
            WeeklyLimit = request.WeeklyLimit,
            MonthlyLimit = request.MonthlyLimit
        };
        var result = await mediator.Send(cmd, cancellationToken);
        if (result.IsFailure) return result.ToHttpResult(context.CorrelationId);
        return TypedResults.Ok(ApiResponse<RecordOperationResultResponse>.Ok(result.Value!, context.CorrelationId));
    }
}
