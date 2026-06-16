namespace BridgeBeats.Contracts.Constants;

/// <summary>
/// Shared constants used by the lookup and queue pipeline.
/// </summary>
public static class LookupConstants {

    /// <summary>
    /// Sentinel value <c>"RATE_LIMITED"</c> published to the completion channel when a provider is
    /// rate-limited. The queue processor publishes it so the lookup orchestrator can return a partial
    /// result immediately rather than waiting for the full timeout.
    /// </summary>
    public const string RateLimitedSentinel = "RATE_LIMITED";

    /// <summary>
    /// Maximum number of queue processing attempts (<c>5</c>) before a message is moved to the
    /// dead-letter queue. Enforced by both <c>QueueProcessorBackgroundService</c> and
    /// <c>SpotifyBatchQueueHelper</c> so both code paths apply the same cap.
    /// </summary>
    public const int MaxQueueRetryAttempts = 5;
}
