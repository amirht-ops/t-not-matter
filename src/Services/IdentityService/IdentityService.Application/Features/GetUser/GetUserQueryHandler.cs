using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Errors;
using IdentityService.Domain.Repositories;
using MediatR;
using SharedKernel.Results;

namespace IdentityService.Application.Features.GetUser;

public sealed class GetUserQueryHandler(IUserRepository users,     Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor)
    : IRequestHandler<GetUserQuery, Result<GetUserResponse>>
{
    public async Task<Result<GetUserResponse>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;
        var user = await users.GetByIdAsync(context.TenantId, request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            return Result<GetUserResponse>.Failure(IdentityErrors.UserNotFound);
        }

        return Result<GetUserResponse>.Success(new GetUserResponse(
            user.Id, user.Username.Value, user.PhoneNumber.Value, user.Status.ToString(),
            user.TenantId));
    }
}