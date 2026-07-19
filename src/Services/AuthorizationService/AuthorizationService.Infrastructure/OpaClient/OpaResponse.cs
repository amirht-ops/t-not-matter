using System.Text.Json.Serialization;

namespace AuthorizationService.Infrastructure.OpaClient;

public sealed record OpaResponse([property: JsonPropertyName("result")] OpaDecisionResult? Result);
public sealed record OpaDecisionResult(
    [property: JsonPropertyName("allow")] bool Allow,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("policy_id")] string? PolicyId,
    [property: JsonPropertyName("policy_version")] string? PolicyVersion);
