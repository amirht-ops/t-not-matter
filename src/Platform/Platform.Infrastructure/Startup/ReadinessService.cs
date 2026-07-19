using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class ReadinessService : IReadinessService
{
    private readonly ReadinessState _state;

    public ReadinessService(ReadinessState state)
    {
        _state = state;
    }

    public Task<ReadinessResult> CheckReadinessAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_state.Latest);
    }
}
