using Platform.Abstractions.Tenant;

namespace Platform.Infrastructure.Tenant;

/// <summary>
/// Ambient request/tenant context. The value is stored in an <see cref="AsyncLocal{T}"/>
/// so it flows correctly through the asynchronous execution context of HTTP requests,
/// MassTransit consumers, and hosted background services (the outbox dispatcher). This
/// guarantees the <see cref="TenantRlsInterceptor"/> can resolve the correct tenant outside
/// of an HTTP request, instead of falling back to <see cref="Guid.Empty"/> (which skips
/// row-level security). Registered as a singleton; the holder itself is stateless.
/// </summary>
public sealed class RequestContextAccessor : IRequestContextAccessor
{
    private static readonly AsyncLocal<RequestContext?> Current = new();

    public RequestContext Context
    {
        get => Current.Value ?? new RequestContext(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), null);
        set => Current.Value = value;
    }
}