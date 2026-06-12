using System.Text.Json.Serialization;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Configuration settings for the queue system.
/// </summary>
public sealed record QueueSettings {
    /// <summary>
    /// Gets the threshold for re-queuing rate-limited requests.
    /// If a Retry-After value exceeds this threshold, the request is queued for later processing.
    /// </summary>
    [JsonPropertyName( "rateLimitRetryThreshold" )]
    public TimeSpan RateLimitRetryThreshold { get; init; } = TimeSpan.FromMinutes( 2 );

    /// <summary>
    /// Gets the number of minutes before an incomplete job/saga expires.
    /// </summary>
    /// <remarks>
    /// Must exceed the Spotify bulk-batch linger backstop (<c>BridgeBeats:Spotify:Batch:LingerMs</c>,
    /// default 24 h / 1 440 min) so that a saga created before a bulk flush is not evicted before
    /// the flush writes its result. Default is 2 880 min (48 h) = 2× the 24 h backstop, giving
    /// visible margin. See <c>spotify-bulk-flush-semantics.md</c> D-decision-1 for the trade-off.
    /// </remarks>
    [JsonPropertyName( "jobExpirationMinutes" )]
    public int JobExpirationMinutes { get; init; } = 2880;

    /// <summary>
    /// Gets the total time budget (in seconds) an interactive caller waits for a complete
    /// lookup result before returning the best available partial result.
    /// </summary>
    [JsonPropertyName( "interactiveWaitSeconds" )]
    public int InteractiveWaitSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the number of dequeue-ordering calls between aging slots.
    /// Every <c>InteractiveAgingInterval</c> calls the dequeue ordering promotes a lower-priority
    /// tier (background or bulk) to the head of the order so that interactive load cannot starve
    /// background and bulk streams indefinitely.
    /// </summary>
    /// <remarks>
    /// Default is 8. Values ≤ 1 are treated as misconfiguration and fall back to the default.
    /// </remarks>
    [JsonPropertyName( "interactiveAgingInterval" )]
    public int InteractiveAgingInterval { get; init; } = 8;

    /// <summary>
    /// Gets the priority aging configuration.
    /// </summary>
    /// <remarks>
    /// The <see cref="PriorityWeights"/> values no longer drive weighted-random selection.
    /// They are retained for backwards-compatible configuration and documentation purposes.
    /// Queue ordering is now deterministic: interactive-first with a bounded aging escape hatch
    /// controlled by <see cref="InteractiveAgingInterval"/>.
    /// </remarks>
    [JsonPropertyName( "weights" )]
    public PriorityWeights Weights { get; init; } = new( );

    /// <summary>
    /// Gets the minimum number of messages required in the bulk queue before
    /// bulk priority requests are processed. This enables batching optimization
    /// for providers that support bulk lookup endpoints.
    /// </summary>
    /// <remarks>
    /// Below this threshold bulk messages are skipped during dequeue; set to 0 or 1 to disable batching. Default is 5.
    /// </remarks>
    [JsonPropertyName( "defaultMinBulkQueueThreshold" )]
    public int DefaultMinBulkQueueThreshold { get; init; } = 5;

    /// <summary>
    /// Gets per-provider minimum bulk queue thresholds.
    /// If a provider is not specified, <see cref="DefaultMinBulkQueueThreshold"/> is used.
    /// </summary>
    [JsonPropertyName( "providerMinBulkThresholds" )]
    public Dictionary<SupportedProviders, int> ProviderMinBulkThresholds { get; init; } = [];

    /// <summary>
    /// Gets the minimum bulk queue threshold for a specific provider.
    /// Returns <see cref="DefaultMinBulkQueueThreshold"/> if not explicitly configured.
    /// </summary>
    /// <param name="provider">The provider to get the threshold for.</param>
    /// <returns>The minimum bulk queue depth required before processing bulk requests.</returns>
    public int GetMinBulkThreshold( SupportedProviders provider )
        => ProviderMinBulkThresholds.TryGetValue( provider, out int threshold )
            ? threshold
            : DefaultMinBulkQueueThreshold;
}
