using FluentValidation;
using MediatR;
using Platform.Abstractions.Tenant;
using PolicyService.Domain.Aggregates.PrincipalHierarchy;
using PolicyService.Domain.Enums;
using PolicyService.Domain.Errors;
using PolicyService.Domain.Repositories;
using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Application.Features.PrincipalHierarchy;

public enum PrincipalHierarchyOperation
{
    UpsertRoleNode = 1,
    UpsertUserNode = 2,
    UpsertEdge = 3,
    RemoveEdge = 4
}

public sealed record UpsertPrincipalEdgeCommand(
    PrincipalHierarchyOperation Operation,
    Guid? NodeId = null,
    Guid? UserId = null,
    Guid? RoleId = null)
    : IRequest<Result<Unit>>, ITransactionalRequest, IIdempotentRequest
{
    public string IdempotencyKey =>
        $"principal-hierarchy:{Operation}:{NodeId}:{UserId}:{RoleId}";
}

public sealed class UpsertPrincipalEdgeCommandValidator : AbstractValidator<UpsertPrincipalEdgeCommand>
{
    public UpsertPrincipalEdgeCommandValidator()
    {
        RuleFor(x => x.Operation).IsInEnum();
        RuleFor(x => x.NodeId).NotNull()
            .When(x => x.Operation is PrincipalHierarchyOperation.UpsertRoleNode or PrincipalHierarchyOperation.UpsertUserNode);
        RuleFor(x => x.UserId).NotNull()
            .When(x => x.Operation is PrincipalHierarchyOperation.UpsertUserNode
                or PrincipalHierarchyOperation.UpsertEdge
                or PrincipalHierarchyOperation.RemoveEdge);
        RuleFor(x => x.RoleId).NotNull()
            .When(x => x.Operation is PrincipalHierarchyOperation.UpsertEdge or PrincipalHierarchyOperation.RemoveEdge);
    }
}

public sealed class UpsertPrincipalEdgeCommandHandler(
    IPrincipalHierarchyRepository hierarchy,
    IUsageLedgerRepository usageLedgers,
    IDebtLedgerRepository debtLedgers,
    IRequestContextAccessor requestContext)
    : IRequestHandler<UpsertPrincipalEdgeCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UpsertPrincipalEdgeCommand command, CancellationToken cancellationToken)
    {
        // ADR-013 Rule 14: TenantId is sourced only from the authenticated JWT (request context),
        // never from an inbound request body/header. The TrustedPrincipalHierarchyConsumer path sets the
        // same context from the signed outbox envelope.
        var tenantId = requestContext.Context.TenantId;
        if (tenantId == Guid.Empty)
            return Result<Unit>.Failure(PolicyErrors.TenantMismatch);

        switch (command.Operation)
        {
            case PrincipalHierarchyOperation.UpsertRoleNode:
                await hierarchy.GetOrCreateNodeAsync(tenantId, command.NodeId!.Value, PrincipalKind.Role, cancellationToken);
                break;

            case PrincipalHierarchyOperation.UpsertUserNode:
                // UC-26: a user principal entered the tenant. Provision its per-user ledgers.
                await hierarchy.GetOrCreateNodeAsync(tenantId, command.UserId!.Value, PrincipalKind.User, cancellationToken);
                var consumerId = ConsumerId.From(command.UserId.Value);
                await usageLedgers.GetOrCreateAsync(tenantId, consumerId, cancellationToken);
                await debtLedgers.GetOrCreateAsync(tenantId, consumerId, cancellationToken);
                break;

            case PrincipalHierarchyOperation.UpsertEdge:
                // UC-27: role assigned to user.
                await hierarchy.GetOrCreateEdgeAsync(tenantId, command.UserId!.Value, command.RoleId!.Value, cancellationToken);
                break;

            case PrincipalHierarchyOperation.RemoveEdge:
                // role-revoked: drop the user→role edge (soft delete).
                var edge = await hierarchy.GetEdgeAsync(tenantId, command.UserId!.Value, command.RoleId!.Value, cancellationToken);
                if (edge is not null)
                    await hierarchy.RemoveEdgeAsync(edge, cancellationToken);
                break;
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
