namespace AuthorizationService.Api.Contracts.Responses;

public sealed record EffectivePermissionsResponse(Guid SubjectId, IReadOnlyCollection<string> Permissions);
