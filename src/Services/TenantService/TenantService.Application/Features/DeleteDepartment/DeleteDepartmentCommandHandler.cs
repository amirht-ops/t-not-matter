using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;
using Unit = SharedKernel.Results.Unit;

namespace TenantService.Application.Features.DeleteDepartment;

public sealed class DeleteDepartmentCommandHandler(
    IDepartmentRepository departments,
    IRequestContextAccessor requestContextAccessor)
    : IRequestHandler<DeleteDepartmentCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(DeleteDepartmentCommand request, CancellationToken cancellationToken)
    {
        var correlationId = requestContextAccessor.Context.CorrelationId;

        var department = await departments.GetByIdAsync(request.TenantId, request.DepartmentId, trackChanges: true, cancellationToken);
        if (department is null)
        {
            return Result<Unit>.Failure(TenantErrors.DepartmentNotFound);
        }

        var result = department.Deactivate(correlationId);
        if (result.IsFailure)
        {
            return Result<Unit>.Failure(result.Error);
        }

        department.SoftDelete(correlationId);

        await departments.UpdateAsync(department, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}