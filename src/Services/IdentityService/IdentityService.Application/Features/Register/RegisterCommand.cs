using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Application;
using SharedKernel.Results;

namespace IdentityService.Application.Features.Register;

public sealed record RegisterCommand(
    string PhoneNumber,
    string Username,
    string Password,
    string Email)
    : IRequest<Result<RegisterResponse>>, IResolveTenantInternally, ITransactionalRequest, IIdempotentRequest
{
    public string IdempotencyKey => $"register:{PhoneNumber}";
}
