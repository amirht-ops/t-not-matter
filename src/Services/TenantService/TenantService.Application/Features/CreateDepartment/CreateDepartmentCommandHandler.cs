using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using TenantService.Domain.Aggregates;
using TenantService.Domain.Errors;
using TenantService.Domain.Repositories;

namespace TenantService.Application.Features.CreateDepartment;

public sealed class CreateDepartmentCommandHandler(
    IDepartmentRepository departments,
    IRequestContextAccessor requestContextAccessor) : IRequestHandler<CreateDepartmentCommand,
    Result<CreateDepartmentResponse>>
{
    public async Task<Result<CreateDepartmentResponse>> Handle(CreateDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var correlationId = requestContextAccessor.Context.CorrelationId;

        var exists = await departments.ExistsByNameAsync(request.TenantId, request.Name, cancellationToken);
        if (exists)
        {
            return Result<CreateDepartmentResponse>.Failure(TenantErrors.DepartmentAlreadyExists);
        }

        var createResult = Department.Create(request.TenantId, request.Name, request.Description, correlationId);
        if (createResult.IsFailure)
        {
            return Result<CreateDepartmentResponse>.Failure(createResult.Error);
        }

        var department = createResult.Value!;
        await departments.AddAsync(department, cancellationToken);
        return Result<CreateDepartmentResponse>.Success(new CreateDepartmentResponse(department.Id));
    }
}
