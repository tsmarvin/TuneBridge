using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Distributed queue for background processing of lookup requests.
/// </summary>
/// <typeparam name="T">The type of request to queue. Must implement <see cref="IQueueableRequest"/>.</typeparam>
/// <remarks>
/// Implementations use Redis Streams with consumer groups for reliable message delivery.
/// Dequeue ordering is deterministic: interactive-first with a bounded aging escape hatch
/// (see <see cref="QueueSettings.InteractiveAgingInterval"/>) that guarantees background and
/// bulk streams are visited at least once every N dequeue-ordering calls, preventing starvation
/// under sustained interactive load.
/// </remarks>
public interface IRequestQueue<T> where T : class, IQueueableRequest {
    /// <summary>
    /// Enqueue a request for background processing.
    /// </summary>
    /// <param name="request">The request to enqueue.</param>
    /// <param name="priority">The priority level for processing order.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous enqueue operation.</returns>
    Task EnqueueAsync( T request, QueuePriority priority, CancellationToken cancellationToken = default );

    /// <summary>
    /// Dequeue the next request, respecting priority weights.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The next queued message, or <c>null</c> if no requests are available.
    /// </returns>
    Task<QueuedMessage<T>?> DequeueAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Dequeue the next request, skipping messages for rate-limited endpoints.
    /// </summary>
    /// <remarks>
    /// Skips messages whose endpoint is currently rate-limited; they remain in the stream until the limit expires.
    /// </remarks>
    /// <param name="rateLimitTracker">The rate limit tracker to check endpoint availability.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The next non-rate-limited message, or <c>null</c> if no eligible requests are available.
    /// </returns>
    Task<QueuedMessage<T>?> DequeueAsync( IRateLimitTracker rateLimitTracker, CancellationToken cancellationToken = default );

    /// <summary>
    /// Acknowledge successful processing of a message.
    /// </summary>
    /// <param name="messageId">The ID of the message to acknowledge.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous acknowledge operation.</returns>
    Task AcknowledgeAsync( string messageId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Return a message to the queue for retry (e.g., transient failure or rate limit).
    /// </summary>
    /// <param name="messageId">The ID of the message to requeue.</param>
    /// <param name="delay">Optional delay before the message becomes available again.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous requeue operation.</returns>
    Task RequeueAsync( string messageId, TimeSpan? delay = null, CancellationToken cancellationToken = default );

    /// <summary>
    /// Get current queue depth by priority.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current queue depth breakdown by priority level.</returns>
    Task<QueueDepth> GetDepthAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Get messages from the Dead Letter Queue.
    /// </summary>
    /// <remarks>
    /// Messages are moved to the DLQ after exceeding maximum retry attempts
    /// or encountering non-recoverable errors. Use this method to inspect
    /// failed messages for debugging or manual intervention.
    /// </remarks>
    /// <param name="limit">Maximum number of messages to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of messages in the DLQ.</returns>
    Task<IReadOnlyList<QueuedMessage<T>>> GetDlqMessagesAsync( int limit, CancellationToken cancellationToken = default );

    /// <summary>
    /// Move a message from the Dead Letter Queue back to the main queue for retry.
    /// </summary>
    /// <remarks>
    /// Use this method to reprocess messages that failed due to transient issues
    /// that have since been resolved (e.g., provider API restored, configuration fixed).
    /// </remarks>
    /// <param name="messageId">The ID of the DLQ message to requeue.</param>
    /// <param name="priority">The priority level for the requeued message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RequeueFromDlqAsync( string messageId, QueuePriority priority, CancellationToken cancellationToken = default );

    /// <summary>
    /// Move a failed message to the Dead Letter Queue.
    /// </summary>
    /// <remarks>
    /// Use this when a message has exceeded retry limits or encountered
    /// a non-recoverable error. The message will be preserved for inspection.
    /// </remarks>
    /// <param name="messageId">The ID of the message to move to DLQ.</param>
    /// <param name="reason">The reason the message is being moved to DLQ.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MoveToDlqAsync( string messageId, string reason, CancellationToken cancellationToken = default );

    /// <summary>
    /// Delete a message from the Dead Letter Queue.
    /// </summary>
    /// <remarks>
    /// Use after inspecting and determining the message should be discarded,
    /// or after successfully reprocessing via <see cref="RequeueFromDlqAsync"/>.
    /// </remarks>
    /// <param name="messageId">The ID of the DLQ message to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the message was deleted; false if not found.</returns>
    Task<bool> DeleteFromDlqAsync( string messageId, CancellationToken cancellationToken = default );
}
