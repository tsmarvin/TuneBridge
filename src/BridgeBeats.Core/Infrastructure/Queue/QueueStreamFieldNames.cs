namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Canonical Redis stream field names for queue wire format.
/// All queue producers and consumers (<c>RedisRequestQueue</c>,
/// <c>SpotifyBulkQueueDecorator</c>, <c>SpotifyBatchQueueHelper</c>) must read
/// and write these exact names so that entries written by one component can be
/// read by another.
/// </summary>
/// <remarks>
/// Keep stream NAMES (e.g. <c>SpotifyConstants.BulkTrackIdStream</c>) in their
/// provider-specific constants class. Only the field names — which are generic
/// queue wire format — live here.
/// </remarks>
public static class QueueStreamFieldNames {

    /// <summary>
    /// Stream entry field that carries the JSON-serialized request payload.
    /// </summary>
    public const string Payload = "payload";

    /// <summary>
    /// Stream entry field that carries the ISO-8601 enqueue timestamp.
    /// Used by size-OR-age flush policies to determine how long an entry has waited.
    /// </summary>
    public const string EnqueuedAt = "enqueuedAt";
}
