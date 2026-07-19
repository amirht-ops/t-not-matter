namespace AuthorizationService.Api.Contracts.Requests;

public sealed record RecordOperationResultRequest(
    Guid RequestId,
    Guid SubjectId,
    string Action,
    string ResourceType,
    string ResourceId,
    int ResourceCount,
    long UsageLimit,
    string UsagePolicyKey)
{
    public long DailyLimit { get; init; }
    public long WeeklyLimit { get; init; }
    public long MonthlyLimit { get; init; }
}
