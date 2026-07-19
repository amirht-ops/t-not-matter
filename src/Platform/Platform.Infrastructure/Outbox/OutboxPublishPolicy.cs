namespace Platform.Infrastructure.Outbox;

/// <summary>
/// Defines the policy for the outbox message processor, including polling interval, batch size, and retry attempts.
/// </summary>
public class OutboxPublishPolicy
{
    /// <summary>
    /// The number of messages to process in a single batch.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// The interval in milliseconds to wait between polling for new messages.
    /// </summary>
    public int PollIntervalMilliseconds { get; set; } = 10000;

    /// <summary>
    /// The duration in seconds to lease a claimed batch of messages.
    /// </summary>
    public int LeaseDurationSeconds { get; set; } = 60;
    
    /// <summary>
    /// The maximum number of retry attempts for a failing message before it is moved to the dead-letter queue.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}
