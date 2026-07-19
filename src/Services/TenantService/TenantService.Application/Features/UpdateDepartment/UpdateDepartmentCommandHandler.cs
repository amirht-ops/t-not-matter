using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;
using Unit = SharedKernel.Results.Unit;

namespace TenantService.Application.Features.UpdateDepartment;

public sealed class UpdateDepartmentCommandHandler(
    IDepartmentRepository departments,
    IRequestContextAccessor requestContextAccessor)
    : IRequestHandler<UpdateDepartmentCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var correlationId = requestContextAccessor.Context.CorrelationId;

        var department = await departments.GetByIdAsync(request.TenantId, request.DepartmentId, trackChanges: true, cancellationToken);
        if (department is null)
        {
            return Result<Unit>.Failure(TenantErrors.DepartmentNotFound);
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var nameResult = department.UpdateName(request.Name, correlationId);
            if (nameResult.IsFailure)
            {
                return Result<Unit>.Failure(nameResult.Error);
            }
        }

        if (request.Description is not null)
        {
            var descResult = department.UpdateDescription(request.Description, correlationId);
            if (descResult.IsFailure)
            {
                return Result<Unit>.Failure(descResult.Error);
            }
        }

        await departments.UpdateAsync(department, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}