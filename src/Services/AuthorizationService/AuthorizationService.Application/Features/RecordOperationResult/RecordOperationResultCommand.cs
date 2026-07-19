using MediatR;
using SharedKernel.Application;
using SharedKernel.Authorization;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.RecordOperationResult;

public sealed record RecordOperationResultCommand(
    Guid RequestId,
    Guid CorrelationId,
    Guid TenantId,
    Guid SubjectId,
    string Action,
    string ResourceType,
    string ResourceId,
    int ResourceCount,
    string UsagePolicyKey,
    long UsageLimit) : IRequest<Result<RecordOperationResultResponse>>, ITransactionalRequest, IAuthorizableRequest
{
    public long DailyLimit { get; init; }
    public long WeeklyLimit { get; init; }
    public long MonthlyLimit { get; init; }

    string IAuthorizableRequest.Action => "operation.record";
    public string Resource => "operation";
}
