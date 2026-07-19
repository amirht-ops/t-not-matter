namespace AuthorizationService.Application.Common.Abstractions;

public interface IAuthorizationDecisionResponse
{
    bool IsAllowed { get; }
    string Decision { get; }
    string ReasonCode { get; }
    string ReasonMessage { get; }
    Guid CorrelationId { get; }
}
