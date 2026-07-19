using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.QuotaPolicies;
using PolicyService.Domain.Enums;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class QuotaPolicyEndpoints
{
    public static IEndpointRouteBuilder MapQuotaPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/quotas")
            .WithTags("Quota Policies")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/", DefineQuotaPolicyAsync)
            .WithName("DefineQuotaPolicy")
            .WithSummary("Define a quota policy for a scope")
            .Produces<ApiResponse<DefineQuotaPolicyResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetQuotaPolicyByIdAsync)
            .WithName("GetQuotaPolicyById")
            .WithSummary("Get a quota policy by id")
            .Produces<ApiResponse<QuotaPolicyDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapGet("/scope", ListQuotaPoliciesByScopeAsync)
            .WithName("ListQuotaPoliciesByScope")
            .WithSummary("List quota policies for a scope")
            .Produces<ApiResponse<IReadOnlyCollection<QuotaPolicyDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapPatch("/{id:guid}", AmendQuotaPolicyAsync)
            .WithName("AmendQuotaPolicy")
            .WithSummary("Amend a quota policy limits")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", RemoveQuotaPolicyAsync)
            .WithName("RemoveQuotaPolicy")
            .WithSummary("Remove a quota policy")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPost("/resolve", ResolveEffectiveQuotaAsync)
            .WithName("ResolveEffectiveQuota")
            .WithSummary("Resolve the effective quota across a candidate scope chain")
            .Produces<ApiResponse<QuotaPolicyDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async ValueTask<IResult> DefineQuotaPolicyAsync(
        DefineQuotaPolicyRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new DefineQuotaPolicyCommand(request.ScopeKind, request.ScopePrincipalId, request.Daily, request.Weekly, request.Monthly);
        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Created(
            $"/api/v1/quotas/{result.Value!.QuotaPolicyId}",
            ApiResponse<DefineQuotaPolicyResponse>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> GetQuotaPolicyByIdAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new GetQuotaPolicyByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<QuotaPolicyDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ListQuotaPoliciesByScopeAsync(
        PrincipalKind scopeKind,
        Guid scopePrincipalId,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new ListQuotaPoliciesByScopeQuery(scopeKind, scopePrincipalId), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<IReadOnlyCollection<QuotaPolicyDto>>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> AmendQuotaPolicyAsync(
        Guid id,
        AmendQuotaPolicyRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new AmendQuotaPolicyCommand(id, request.Daily, request.Weekly, request.Monthly);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> RemoveQuotaPolicyAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new RemoveQuotaPolicyCommand(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ResolveEffectiveQuotaAsync(
        ResolveEffectiveQuotaRequest request,
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
                new ApiError("Quota.InvalidScope", "One or more candidate scopes are invalid."), correlationId));

        var result = await mediator.Send(
            new ResolveEffectiveQuotaQuery(candidateScopes.Select(s => s.Value!).ToList()), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<QuotaPolicyDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    public sealed record DefineQuotaPolicyRequest(
        PrincipalKind ScopeKind,
        Guid ScopePrincipalId,
        long Daily,
        long Weekly,
        long Monthly);

    public sealed record AmendQuotaPolicyRequest(long Daily, long Weekly, long Monthly);

    public sealed record ResolveEffectiveQuotaRequest(IReadOnlyCollection<ScopeCandidate> CandidateScopes);

    public sealed record ScopeCandidate(PrincipalKind Kind, Guid PrincipalId);
}
