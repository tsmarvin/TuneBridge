namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Status information about the cache bootstrap background service.
/// </summary>
public sealed class CacheBootstrapStatus {

    /// <summary>
    /// Redis key used to store and retrieve the cache bootstrap status.
    /// </summary>
    public const string RedisKey = "status:cache-bootstrap";

    /// <summary>
    /// Timestamp of the last completed bootstrap run.
    /// </summary>
    public DateTimeOffset? LastRunTime { get; init; }

    /// <summary>
    /// Timestamp when the next bootstrap run is scheduled.
    /// </summary>
    public DateTimeOffset? NextScheduledRun { get; init; }

    /// <summary>
    /// Whether a bootstrap operation is currently in progress.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Number of records successfully processed in the last run.
    /// </summary>
    public int? LastSuccessCount { get; init; }

    /// <summary>
    /// Number of errors encountered in the last run.
    /// </summary>
    public int? LastErrorCount { get; init; }

    /// <summary>
    /// Duration of the last run in seconds.
    /// </summary>
    public double? LastDurationSeconds { get; init; }

    /// <summary>
    /// Total number of keys in the Redis cache.
    /// </summary>
    public long? RedisKeyCount { get; init; }
}
