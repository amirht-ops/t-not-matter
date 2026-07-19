namespace SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Outbox Pattern (Coding Rules – Messaging Rules: mandatory).
/// Prevents lost messages for all critical domain events.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }          
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;  // JSON
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? NextRetryAt { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;

    public void MarkFailed(string error)
    {
        Error = error;
        RetryCount++;
        NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, RetryCount));
    }
}
