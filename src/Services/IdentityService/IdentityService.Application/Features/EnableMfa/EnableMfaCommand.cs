using MediatR;
using SharedKernel.Application;
using UnitResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.EnableMfa;

public sealed record EnableMfaCommand(Guid UserId, string Secret) : IRequest<UnitResult>, ITransactionalRequest;
