using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.Services;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Policies;

public sealed record PublishPolicyCommand(Guid PolicyId)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest
{
    public string Action => "policy.publish";
    public string Resource => $"policy:{PolicyId:N}";
}

public sealed class PublishPolicyCommandValidator : AbstractValidator<PublishPolicyCommand>
{
    public PublishPolicyCommandValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}

public sealed class PublishPolicyCommandHandler(
    IPolicyRepository policies,
    IRegoGenerationService regoGenerator,
    IRequestContextAccessor requestContext)
    : IRequestHandler<PublishPolicyCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(PublishPolicyCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var policy = await policies.GetByIdAsync(context.TenantId, PolicyId.From(command.PolicyId), trackChanges: true, cancellationToken: cancellationToken);
        if (policy is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        var rego = regoGenerator.Generate(policy.Expression, policy.Condition);
        if (rego.IsFailure)
            return Result<Unit>.Failure(rego.Error);

        var setRego = policy.SetCompiledRego(rego.Value!);
        if (setRego.IsFailure)
            return Result<Unit>.Failure(setRego.Error);

        var publish = policy.Publish(context.CorrelationId);
        if (publish.IsFailure)
            return Result<Unit>.Failure(publish.Error);

        await policies.UpdateAsync(policy, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
