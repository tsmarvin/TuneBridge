namespace BridgeBeats.Contracts.Enums;

/// <summary>
/// Scheduling priority lane for a queued request. Each lane maps to its own stream.
/// </summary>
/// <remarks>
/// Lanes are drained by a weighted dequeue order that favors higher-priority work but periodically
/// promotes lower-priority lanes to prevent starvation, and the bulk lane is additionally gated by a
/// minimum-depth threshold. The order is therefore not a strict <see cref="Interactive"/>-then-
/// <see cref="Background"/>-then-<see cref="Bulk"/> sequence.
/// </remarks>
public enum QueuePriority {

    /// <summary>
    /// User-initiated requests that need a fast response. Generally dequeued first.
    /// </summary>
    Interactive = 0,

    /// <summary>
    /// Default priority for saga and queue work with no live caller waiting.
    /// </summary>
    Background = 1,

    /// <summary>
    /// Lowest priority. Bulk operations such as Jetstream processing, drained behind the other lanes
    /// and gated by a minimum queue depth.
    /// </summary>
    Bulk = 2
}
