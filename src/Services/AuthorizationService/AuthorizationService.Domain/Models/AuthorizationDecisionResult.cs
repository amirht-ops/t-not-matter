using SharedKernel.Authorization;

namespace AuthorizationService.Domain.Models;

public sealed record AuthorizationDecisionResult(
    bool IsAllowed,
    AuthorizationDecisionState Decision,
    string ReasonCode,
    string ReasonMessage,
    string? PolicyId,
    string? PolicyVersion,
    DateTimeOffset EvaluatedAtUtc,
    long EvaluationDurationMs,
    Guid CorrelationId,
    Guid RequestId)
{
    public static AuthorizationDecisionResult Deny(string reasonCode, string reasonMessage, Guid correlationId, long durationMs = 0, DateTimeOffset? evaluatedAtUtc = null, Guid requestId = default) =>
        new(false, AuthorizationDecisionState.Deny, reasonCode, reasonMessage, null, null, evaluatedAtUtc ?? DateTimeOffset.UtcNow, durationMs, correlationId, requestId);

    public static AuthorizationDecisionResult Allow(string reasonMessage, Guid correlationId, long durationMs, string? policyId, string? policyVersion, DateTimeOffset evaluatedAtUtc, Guid requestId = default) =>
        new(true, AuthorizationDecisionState.Allow, AuthorizationDecisionReason.Allowed, reasonMessage, policyId, policyVersion, evaluatedAtUtc, durationMs, correlationId, requestId);

    public static AuthorizationDecisionResult NotApplicable(string reasonCode, string reasonMessage, Guid correlationId, long durationMs = 0, DateTimeOffset? evaluatedAtUtc = null, Guid requestId = default) =>
        new(false, AuthorizationDecisionState.NotApplicable, reasonCode, reasonMessage, null, null, evaluatedAtUtc ?? DateTimeOffset.UtcNow, durationMs, correlationId, requestId);

    public static AuthorizationDecisionResult Indeterminate(string reasonCode, string reasonMessage, Guid correlationId, long durationMs = 0, DateTimeOffset? evaluatedAtUtc = null, Guid requestId = default) =>
        new(false, AuthorizationDecisionState.Indeterminate, reasonCode, reasonMessage, null, null, evaluatedAtUtc ?? DateTimeOffset.UtcNow, durationMs, correlationId, requestId);
}
