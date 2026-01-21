namespace BridgeBeats.Contracts.Enums;

/// <summary>
/// Priority level for queued requests.
/// </summary>
/// <remarks>
/// Priority weighting is applied during dequeue operations.
/// Higher-priority requests are processed before lower-priority ones
/// based on configured weight ratios.
/// </remarks>
public enum QueuePriority {
    /// <summary>
    /// User-initiated requests requiring fast response.
    /// These are processed with highest priority.
    /// </summary>
    Interactive = 0,

    /// <summary>
    /// Background refresh of stale cache entries.
    /// Processed when no interactive requests are pending.
    /// </summary>
    Background = 1,

    /// <summary>
    /// Bulk operations like Jetstream processing.
    /// Lowest priority, processed during idle periods.
    /// </summary>
    Bulk = 2
}
