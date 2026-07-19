namespace AuthorizationService.Application.Common.Abstractions;

public sealed class AuthorizationOptions
{
    public int MaxBatchSize { get; init; } = 50;
    public bool EnableDecisionCache { get; init; }
}
