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
}
