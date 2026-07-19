using MediatR;
using Platform.Abstractions.Locking;
using Platform.Abstractions.Tenant;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Errors;
using SharedKernel.Results;
using TenantService.Domain.Aggregates;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;
using TenantService.Domain.ValueObjects;

namespace TenantService.Application.Features.CreateTenant;

public sealed class CreateTenantCommandHandler(
    ITenantRepository tenantRepository,
    IRequestContextAccessor requestContextAccessor,
    IDistributedLockService lockService)
    : IRequestHandler<CreateTenantCommand, Result<Guid>>
{
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);

    public async Task<Result<Guid>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenantCodeResult = TenantCode.FromString(request.Identifier);
        if (tenantCodeResult.IsFailure)
            return Result<Guid>.Failure(tenantCodeResult.Error);

        var tenantCode = tenantCodeResult.Value!;

        var slugResult = TenantSlug.Create(request.Slug);
        if (slugResult.IsFailure)
            return Result<Guid>.Failure(slugResult.Error);

        var slug = slugResult.Value!;

        var lockKey = $"create-tenant:{request.Identifier}:{request.Slug}";

        IDistributedLock? lockHandle;
        try
        {
            lockHandle = await lockService.AcquireLockAsync(lockKey, LockExpiry, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Guid>.Failure(
                Error.Failure("Tenant.LockServiceUnavailable",
                    $"The distributed lock service is temporarily unavailable. Please retry the request. Detail: {ex.Message}"));
        }

        if (lockHandle is null || !lockHandle.IsAcquired)
            return Result<Guid>.Failure(TenantErrors.IdentifierAlreadyExists);

        await using var lockHandleScope = lockHandle;

        var exists = await tenantRepository.ExistsByCodeAsync(tenantCode, cancellationToken);
        if (exists)
            return Result<Guid>.Failure(TenantErrors.IdentifierAlreadyExists);

        var slugExists = await tenantRepository.ExistsBySlugAsync(slug, cancellationToken);
        if (slugExists)
            return Result<Guid>.Failure(TenantErrors.SlugAlreadyExists);

        var correlationId = requestContextAccessor.Context.CorrelationId;
        var createResult = Tenant.Create(request.Name, request.Identifier, slug, correlationId);
        if (createResult.IsFailure)
            return Result<Guid>.Failure(createResult.Error);

        var tenant = createResult.Value!;
        await tenantRepository.AddAsync(tenant, cancellationToken);
        return Result<Guid>.Success(tenant.Id);
    }
}
