using AuthorizationService.Application.Common.Abstractions;
using AuthorizationService.Domain.Aggregates.UsageTracking;
using AuthorizationService.Domain.Repositories;
using AuthorizationService.Domain.Services;
using AuthorizationService.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Results;

namespace AuthorizationService.Application.Features.RecordOperationResult;

public sealed class RecordOperationResultCommandHandler(
    IUsageTrackingRepository repository,
    IRealTimeUsageStore realTimeUsageStore,
    ILogger<RecordOperationResultCommandHandler> logger) : IRequestHandler<RecordOperationResultCommand, Result<RecordOperationResultResponse>>
{
    public async Task<Result<RecordOperationResultResponse>> Handle(RecordOperationResultCommand request, CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId;
        var subjectId = SubjectId.From(request.SubjectId);

        var dailyWindow = TimeWindow.Daily(DateTimeOffset.UtcNow);
        var weeklyWindow = TimeWindow.Weekly(DateTimeOffset.UtcNow);
        var monthlyWindow = TimeWindow.Monthly(DateTimeOffset.UtcNow);

        var dailyLimit = request.DailyLimit > 0 ? request.DailyLimit : request.UsageLimit;
        var weeklyLimit = request.WeeklyLimit > 0 ? request.WeeklyLimit : request.UsageLimit * 7;
        var monthlyLimit = request.MonthlyLimit > 0 ? request.MonthlyLimit : request.UsageLimit * 30;

        var incremented = await repository.TryAtomicIncrementAsync(
            tenantId, subjectId, request.Action, request.ResourceType, request.ResourceId,
            dailyWindow, cancellationToken);

        if (!incremented)
        {
            var tracking = UsageTracking.Create(
                tenantId, subjectId, request.Action, request.ResourceType, request.ResourceId,
                dailyWindow, request.CorrelationId);

            await repository.AddAsync(tracking, cancellationToken);
        }

        var quota = await realTimeUsageStore.LoadQuotaSnapshotAsync(
            tenantId, subjectId, request.Action, request.ResourceType, request.ResourceId,
            dailyWindow, weeklyWindow, monthlyWindow,
            dailyLimit, weeklyLimit, monthlyLimit,
            cancellationToken);

        var updatedQuota = quota.ApplyConsumption(request.ResourceCount);

        await realTimeUsageStore.SaveQuotaSnapshotAsync(
            tenantId, subjectId, request.Action, request.ResourceType, request.ResourceId,
            updatedQuota,
            monthlyLimit,
            dailyWindow, weeklyWindow, monthlyWindow,
            cancellationToken);

        logger.LogInformation(
            "Usage recorded tenant={TenantId} subject={SubjectId} action={Action} resource={ResourceType}/{ResourceId} " +
            "daily={Daily} weekly={Weekly} monthly={Monthly} debt={Debt}",
            request.TenantId, request.SubjectId, request.Action, request.ResourceType, request.ResourceId,
            updatedQuota.DailyRemaining, updatedQuota.WeeklyRemaining, updatedQuota.MonthlyRemaining, updatedQuota.OutstandingDebt());

        if (updatedQuota.HasMonthlyDebt())
        {
            logger.LogWarning(
                "Monthly quota exceeded tenant={TenantId} subject={SubjectId} action={Action} debt={Debt}",
                tenantId, subjectId.Value, request.Action, updatedQuota.OutstandingDebt());
        }

        return Result<RecordOperationResultResponse>.Success(new RecordOperationResultResponse(true));
    }
}
