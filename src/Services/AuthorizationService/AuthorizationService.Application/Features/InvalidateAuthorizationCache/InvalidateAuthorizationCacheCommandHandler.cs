using MediatR;
using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.ValueObjects;
using Platform.Abstractions.Tenant;
using SharedKernel.Results;
using Unit = SharedKernel.Results.Unit;

namespace AuthorizationService.Application.Features.InvalidateAuthorizationCache;

public sealed class InvalidateAuthorizationCacheCommandHandler(IAuthorizationCache cache, IAuthorizationAuditSink auditSink, IRequestContextAccessor requestContext) : IRequestHandler<InvalidateAuthorizationCacheCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(InvalidateAuthorizationCacheCommand command, CancellationToken cancellationToken)
    {
        var context = requestContext.Context;
        var tenantId = context.TenantId;
        var subjectId = SubjectId.From(command.SubjectId);
        await cache.InvalidateSubjectAsync(tenantId, subjectId, cancellationToken);
        await auditSink.RecordStateChangeAsync("AuthorizationService.cache_invalidated", context.TenantId, context.CorrelationId, command.SubjectId, true, null, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
