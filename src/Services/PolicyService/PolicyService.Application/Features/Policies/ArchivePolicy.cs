using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.Policies;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Authorization;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace PolicyService.Application.Features.Policies;

public sealed record ArchivePolicyCommand(Guid PolicyId)
    : IRequest<Result<Unit>>, ITransactionalRequest, IAuthorizableRequest
{
    public string Action => "policy.archive";
    public string Resource => $"policy:{PolicyId:N}";
}

public sealed class ArchivePolicyCommandValidator : AbstractValidator<ArchivePolicyCommand>
{
    public ArchivePolicyCommandValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}

public sealed class ArchivePolicyCommandHandler(IPolicyRepository policies, IRequestContextAccessor requestContext)
    : IRequestHandler<ArchivePolicyCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ArchivePolicyCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;

        var policy = await policies.GetByIdAsync(context.TenantId, PolicyId.From(command.PolicyId), trackChanges: true, cancellationToken: cancellationToken);
        if (policy is null)
            return Result<Unit>.Failure(GeneralErrors.NotFound);

        var archive = policy.Archive(context.CorrelationId);
        if (archive.IsFailure)
            return Result<Unit>.Failure(archive.Error);

        await policies.UpdateAsync(policy, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
