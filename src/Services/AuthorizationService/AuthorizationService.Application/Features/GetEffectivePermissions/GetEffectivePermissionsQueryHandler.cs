using MediatR;
using AuthorizationService.Domain.Services;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.GetEffectivePermissions;

public sealed class GetEffectivePermissionsQueryHandler(IEffectivePermissionResolver resolver, IRequestContextAccessor requestContext) : IRequestHandler<GetEffectivePermissionsQuery, Result<GetEffectivePermissionsResponse>>
{
    public async Task<Result<GetEffectivePermissionsResponse>> Handle(GetEffectivePermissionsQuery query, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var subjectId = SubjectId.From(query.SubjectId);
        var permissions = await resolver.ResolveAsync(tenantId, subjectId, cancellationToken);
        return Result<GetEffectivePermissionsResponse>.Success(new GetEffectivePermissionsResponse(query.SubjectId, permissions));
    }
}
