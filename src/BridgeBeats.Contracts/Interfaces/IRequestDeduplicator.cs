using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Prevents duplicate in-flight requests across all service instances.
/// </summary>
/// <remarks>
/// Uses distributed locking (e.g., Redis SETNX with TTL) to ensure only one instance
/// processes a given request at a time. Other instances can subscribe to completion
/// notifications to receive results without making duplicate API calls.
/// </remarks>
public interface IRequestDeduplicator {
    /// <summary>
    /// Attempt to acquire exclusive processing rights for a request.
    /// </summary>
    /// <param name="requestKey">A unique key identifying the request (e.g., "isrc:US1234567890").</param>
    /// <param name="timeout">How long to hold the lock before it auto-expires.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A result indicating whether this instance acquired the lock,
    /// or if another instance is already processing the request.
    /// </returns>
    Task<DeduplicationResult> TryAcquireAsync( string requestKey, TimeSpan timeout, CancellationToken cancellationToken = default );

    /// <summary>
    /// Release processing rights and notify any waiters of completion.
    /// </summary>
    /// <param name="requestKey">The unique key for the request being released.</param>
    /// <param name="resultUri">The ATProto URI of the result, or <c>null</c> if the request failed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous release operation.</returns>
    Task ReleaseAsync( string requestKey, string? resultUri, CancellationToken cancellationToken = default );

    /// <summary>
    /// Subscribe to completion notification for an in-flight request.
    /// </summary>
    /// <param name="requestKey">The unique key for the request to wait for.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The ATProto URI of the result if the request completed successfully,
    /// or <c>null</c> if the request failed or the timeout was reached.
    /// </returns>
    Task<string?> WaitForCompletionAsync( string requestKey, TimeSpan timeout, CancellationToken cancellationToken = default );
}
