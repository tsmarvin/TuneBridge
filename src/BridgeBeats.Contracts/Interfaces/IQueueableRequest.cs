namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Base contract for anything that can be enqueued on a provider request queue, exposing the
/// fields the queue needs to identify, correlate, age, and retry a request.
/// </summary>
/// <remarks>
/// Implemented by <c>QueuedLookupRequest</c> and consumed by <see cref="IRequestQueue{T}"/>,
/// which constrains its element type to this interface. Implementations must be serializable to
/// JSON for storage in Redis streams.
/// </remarks>
public interface IQueueableRequest {

    /// <summary>
    /// The unique identifier of this request.
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// The id of the lookup saga (or job) this request is a leg of.
    /// </summary>
    string SagaId { get; }

    /// <summary>
    /// When the request was originally created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// How many times this request has been attempted, used for retry and dead-letter decisions.
    /// </summary>
    int AttemptCount { get; }
}
