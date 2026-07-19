using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.Subscriptions;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/subscriptions")
            .WithTags("Subscriptions")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/", AssignSubscriptionAsync)
            .WithName("AssignSubscription")
            .WithSummary("Assign a policy subscription to a scope")
            .Produces<ApiResponse<AssignSubscriptionResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetSubscriptionByIdAsync)
            .WithName("GetSubscriptionById")
            .WithSummary("Get a subscription by id")
            .Produces<ApiResponse<SubscriptionDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/policy/{policyId:guid}", ListSubscriptionsByPolicyAsync)
            .WithName("ListSubscriptionsByPolicy")
            .WithSummary("List subscriptions for a policy")
            .Produces<ApiResponse<IReadOnlyCollection<SubscriptionDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/activate", ActivateSubscriptionAsync)
            .WithName("ActivateSubscription")
            .WithSummary("Activate a subscription")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/revoke", RevokeSubscriptionAsync)
            .WithName("RevokeSubscription")
            .WithSummary("Revoke a subscription")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/supersede", SupersedeSubscriptionAsync)
            .WithName("SupersedeSubscription")
            .WithSummary("Supersede a subscription")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPost("/resolve", ResolveEffectiveSubscriptionAsync)
            .WithName("ResolveEffectiveSubscription")
            .WithSummary("Resolve the effective subscription across a candidate scope chain")
            .Produces<ApiResponse<SubscriptionDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async ValueTask<IResult> AssignSubscriptionAsync(
        AssignSubscriptionRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new AssignSubscriptionCommand(request.PolicyId, request.ScopeKind, request.ScopePrincipalId);
        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Created(
            $"/api/v1/subscriptions/{result.Value!.SubscriptionId}",
            ApiResponse<AssignSubscriptionResponse>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> GetSubscriptionByIdAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new GetSubscriptionByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<SubscriptionDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ListSubscriptionsByPolicyAsync(
        Guid policyId,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new ListSubscriptionsByPolicyQuery(policyId), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<IReadOnlyCollection<SubscriptionDto>>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ActivateSubscriptionAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new ActivateSubscriptionCommand(id), cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> RevokeSubscriptionAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new RevokeSubscriptionCommand(id), cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> SupersedeSubscriptionAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new SupersedeSubscriptionCommand(id), cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ResolveEffectiveSubscriptionAsync(
        ResolveEffectiveSubscriptionRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var candidateScopes = request.CandidateScopes
            .Select(s => SubscriptionScope.Create(s.Kind, s.PrincipalId))
            .ToList();
        if (candidateScopes.Any(s => s.IsFailure))
            return TypedResults.BadRequest(ApiResponse<object>.Fail(
                new ApiError("Subscription.InvalidScope", "One or more candidate scopes are invalid."), correlationId));

        var result = await mediator.Send(
            new ResolveEffectiveSubscriptionQuery(candidateScopes.Select(s => s.Value!).ToList()), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<SubscriptionDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    public sealed record AssignSubscriptionRequest(Guid PolicyId, PrincipalKind ScopeKind, Guid ScopePrincipalId);

    public sealed record ResolveEffectiveSubscriptionRequest(IReadOnlyCollection<ScopeCandidate> CandidateScopes);

    public sealed record ScopeCandidate(PrincipalKind Kind, Guid PrincipalId);
}
