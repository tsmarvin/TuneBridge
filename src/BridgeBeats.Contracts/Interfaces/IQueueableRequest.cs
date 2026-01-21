namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Marker interface for requests that can be queued for background processing.
/// </summary>
/// <remarks>
/// Implementations must be serializable to JSON for Redis stream storage.
/// </remarks>
public interface IQueueableRequest {
    /// <summary>
    /// Gets the unique identifier for this request.
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// Gets the saga/job identifier this request belongs to.
    /// </summary>
    string SagaId { get; }

    /// <summary>
    /// Gets the timestamp when this request was originally created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the number of times this request has been attempted.
    /// </summary>
    int AttemptCount { get; }
}
