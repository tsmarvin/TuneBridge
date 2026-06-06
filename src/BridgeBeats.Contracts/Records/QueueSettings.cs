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
    [JsonPropertyName( "jobExpirationMinutes" )]
    public int JobExpirationMinutes { get; init; } = 60;

    /// <summary>
    /// Gets the total time budget (in seconds) an interactive caller waits for a complete
    /// lookup result before returning the best available partial result.
    /// </summary>
    [JsonPropertyName( "interactiveWaitSeconds" )]
    public int InteractiveWaitSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the priority weighting configuration.
    /// </summary>
    [JsonPropertyName( "weights" )]
    public PriorityWeights Weights { get; init; } = new( );

    /// <summary>
    /// Gets the minimum number of messages required in the bulk queue before
    /// bulk priority requests are processed. This enables batching optimization
    /// for providers that support bulk lookup endpoints.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the bulk queue depth is below this threshold, bulk messages are skipped
    /// during dequeue until the threshold is reached. Once reached, bulk messages
    /// are processed according to normal priority weighting.
    /// </para>
    /// <para>
    /// Set to 0 or 1 to process bulk requests immediately (no batching).
    /// Default is 5.
    /// </para>
    /// </remarks>
    [JsonPropertyName( "defaultMinBulkQueueThreshold" )]
    public int DefaultMinBulkQueueThreshold { get; init; } = 5;

    /// <summary>
    /// Gets per-provider minimum bulk queue thresholds.
    /// If a provider is not specified, <see cref="DefaultMinBulkQueueThreshold"/> is used.
    /// </summary>
    /// <example>
    /// <code>
    /// // Configuration example:
    /// {
    ///   "providerMinBulkThresholds": {
    ///     "AppleMusic": 5,
    ///     "Spotify": 20,
    ///     "Tidal": 5
    ///   }
    /// }
    /// </code>
    /// </example>
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
