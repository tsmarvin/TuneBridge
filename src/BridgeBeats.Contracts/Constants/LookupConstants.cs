namespace BridgeBeats.Contracts.Constants;

/// <summary>
/// Constants used across the lookup pipeline.
/// </summary>
public static class LookupConstants {
    /// <summary>
    /// Sentinel value published to the completion channel when a provider is rate-limited.
    /// The queue processor publishes this so that the lookup orchestrator can return a partial
    /// result immediately rather than waiting for the full timeout.
    /// </summary>
    public const string RateLimitedSentinel = "RATE_LIMITED";

    /// <summary>
    /// Maximum number of times a queued lookup message may be retried before it is
    /// considered complete-failed and discarded. Shared by
    /// <c>QueueProcessorBackgroundService</c> and <c>SpotifyBatchQueueHelper</c>
    /// so both code paths enforce the same cap.
    /// </summary>
    public const int MaxQueueRetryAttempts = 5;
}
