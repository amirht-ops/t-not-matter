using MediatR;
using Platform.Abstractions.Authorization;
using Platform.Abstractions.Tenant;
using PolicyService.Api.Extensions;
using PolicyService.Application.Features.Policies;
using SharedKernel.Responses;

namespace PolicyService.Api.Endpoints;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/policies")
            .WithTags("Policies")
            .RequireAuthorization(PlatformAuthorizationPolicies.PlatformServiceOnly);

        group.MapPost("/", CreatePolicyAsync)
            .WithName("CreatePolicy")
            .WithSummary("Create a new policy")
            .Produces<ApiResponse<CreatePolicyResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict);

        group.MapGet("/", ListPoliciesAsync)
            .WithName("ListPolicies")
            .WithSummary("List all policies for the current tenant")
            .Produces<ApiResponse<IReadOnlyCollection<PolicyDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetPolicyByIdAsync)
            .WithName("GetPolicyById")
            .WithSummary("Get a policy by id")
            .Produces<ApiResponse<PolicyDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/publish", PublishPolicyAsync)
            .WithName("PublishPolicy")
            .WithSummary("Publish a policy")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/archive", ArchivePolicyAsync)
            .WithName("ArchivePolicy")
            .WithSummary("Archive a policy")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        group.MapPatch("/{id:guid}/condition", ChangePolicyConditionAsync)
            .WithName("ChangePolicyCondition")
            .WithSummary("Change a policy condition and expression")
            .Produces<ApiResponse<Unit>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async ValueTask<IResult> CreatePolicyAsync(
        CreatePolicyRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new CreatePolicyCommand(request.Name, request.Condition, request.Expression, request.Priority);
        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return result.ToHttpResult(correlationId);

        return TypedResults.Created(
            $"/api/v1/policies/{result.Value!.PolicyId}",
            ApiResponse<CreatePolicyResponse>.Ok(result.Value!, correlationId));
    }

    private static async ValueTask<IResult> ListPoliciesAsync(
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new ListPoliciesByTenantQuery(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<IReadOnlyCollection<PolicyDto>>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> GetPolicyByIdAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new GetPolicyByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(ApiResponse<PolicyDto>.Ok(result.Value!, correlationId))
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> PublishPolicyAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new PublishPolicyCommand(id), cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ArchivePolicyAsync(
        Guid id,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var result = await mediator.Send(new ArchivePolicyCommand(id), cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    private static async ValueTask<IResult> ChangePolicyConditionAsync(
        Guid id,
        ChangePolicyConditionRequest request,
        IMediator mediator,
        IRequestContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var correlationId = accessor.Context.CorrelationId;
        var command = new ChangePolicyConditionCommand(id, request.Condition, request.Expression);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? ResultHttpExtensions.ToOkHttpResult(SharedKernel.Results.Unit.Value, correlationId)
            : result.ToHttpResult(correlationId);
    }

    public sealed record CreatePolicyRequest(string Name, string Condition, string Expression, int Priority = 100);

    public sealed record ChangePolicyConditionRequest(string Condition, string Expression);
}
