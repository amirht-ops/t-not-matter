using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.CreatePermission;

public sealed record CreatePermissionCommand(string Key, string Action, string ResourceType, string? Description) : IRequest<Result<CreatePermissionResponse>>, ITransactionalRequest, IIdempotentRequest, IAuthorizableRequest
{
    public string IdempotencyKey => $"create-permission:{Key}";
    string IAuthorizableRequest.Action => "permission.create";
    public string Resource => $"permission:{Key}";
}
