namespace AuthorizationService.Api.Contracts.Requests;

public sealed record EvaluateAuthorizationRequest(Guid? SubjectId, string Action, string ResourceType, string ResourceId, Guid? OwnerId, IReadOnlyDictionary<string, string>? ResourceAttributes, IReadOnlyDictionary<string, string>? EnvironmentAttributes, IReadOnlyDictionary<string, string>? UsageAttributes);
