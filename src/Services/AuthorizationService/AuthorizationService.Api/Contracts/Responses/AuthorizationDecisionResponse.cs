namespace AuthorizationService.Api.Contracts.Responses;

public sealed record AuthorizationDecisionResponse(bool IsAllowed, string Decision, string ReasonCode, string ReasonMessage, string? PolicyId, string? PolicyVersion, DateTimeOffset EvaluatedAtUtc, long EvaluationDurationMs, Guid CorrelationId, Guid RequestId);
