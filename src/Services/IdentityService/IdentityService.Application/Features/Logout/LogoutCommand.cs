using MediatR;
using SharedKernel.Application;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace IdentityService.Application.Features.Logout;

public sealed record LogoutCommand(Guid SessionId) : IRequest<Result<Unit>>, ITransactionalRequest;