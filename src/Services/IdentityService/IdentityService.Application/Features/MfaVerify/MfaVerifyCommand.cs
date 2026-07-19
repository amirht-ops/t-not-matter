using MediatR;
using SharedKernel.Application;
using UnitResult = SharedKernel.Results.Result<SharedKernel.Results.Unit>;

namespace IdentityService.Application.Features.MfaVerify;

public sealed record MfaVerifyCommand(Guid UserId, string Code) : IRequest<UnitResult>, ITransactionalRequest;
