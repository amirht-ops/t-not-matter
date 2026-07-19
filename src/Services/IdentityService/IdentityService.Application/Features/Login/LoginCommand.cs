using MediatR;
using Platform.Abstractions.Tenant;
using SharedKernel.Application;
using SharedKernel.Results;

namespace IdentityService.Application.Features.Login;

public sealed record LoginCommand(string? Identifier, string? Username, string Password, string? MfaCode, string? IpAddress, string? UserAgent)
    : IRequest<Result<LoginResponse>>, IResolveTenantInternally, ITransactionalRequest, IRetryableRequest;