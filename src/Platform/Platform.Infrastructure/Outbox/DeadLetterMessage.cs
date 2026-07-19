namespace Platform.Infrastructure.Outbox;

/// <summary>
/// Represents a message that has failed processing and has been moved to a dead-letter queue for investigation.
/// </summary>
public class DeadLetterMessage
{
    /// <summary>
    /// The unique identifier for the dead-letter message.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The ID of the original outbox message that failed.
    /// </summary>
    public Guid OutboxMessageId { get; set; }

    /// <summary>
    /// The type of the event that failed.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The JSON payload of the original message.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// The error message captured during the last processing attempt.
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// The tenant associated with the original message.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The correlation ID of the original message.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// The timestamp when the message was moved to the dead-letter queue.
    /// </summary>
    public DateTimeOffset MovedAt { get; set; }
}
