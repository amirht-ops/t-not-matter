using AuthorizationService.Application.Common.Abstractions;

namespace AuthorizationService.Application.Features.EvaluateAuthorizationDecision;

public sealed record EvaluateAuthorizationDecisionResponse(bool IsAllowed, string Decision, string ReasonCode, string ReasonMessage, string? PolicyId, string? PolicyVersion, Guid CorrelationId, DateTimeOffset EvaluatedAtUtc, long EvaluationDurationMs, Guid RequestId) : IAuthorizationDecisionResponse;
