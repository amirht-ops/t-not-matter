using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Application;
using SharedKernel.Results;

namespace IdentityService.Application.Features.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<RefreshTokenResponse>>, IResolveTenantInternally, ITransactionalRequest;
