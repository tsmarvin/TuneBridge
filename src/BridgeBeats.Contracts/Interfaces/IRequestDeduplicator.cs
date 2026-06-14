using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Single-flight coordination for identical concurrent lookups: one caller acquires the right
/// to process a request key while others wait for its published result rather than duplicating
/// the work.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>RedisRequestDeduplicator</c>
/// (<c>Infrastructure/Queue/RedisRequestDeduplicator.cs</c>), using distributed locking (Redis
/// SETNX with a TTL) so the coordination holds across all service instances. The winning caller
/// acquires via <see cref="TryAcquireAsync"/>, processes the request, then publishes the result
/// AT-URI via <see cref="ReleaseAsync"/>; waiting callers receive that AT-URI from the wait
/// methods.
/// </remarks>
public interface IRequestDeduplicator {
    /// <summary>
    /// Attempts to acquire the single-flight lock for a request key.
    /// </summary>
    /// <param name="requestKey">The key identifying the logical request to deduplicate (for example <c>isrc:US1234567890</c>).</param>
    /// <param name="timeout">How long the acquired lock is held before it auto-expires.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is a <see cref="DeduplicationResult"/>: <c>Acquired</c> is
    /// <see langword="true"/> when this caller won the lock and must do the work;
    /// <c>AlreadyInFlight</c> is <see langword="true"/> when another caller is already processing
    /// the same key and this caller should wait instead.
    /// </returns>
    Task<DeduplicationResult> TryAcquireAsync( string requestKey, TimeSpan timeout, CancellationToken cancellationToken = default );

    /// <summary>
    /// Releases the single-flight lock for a request key and publishes its result so waiting
    /// callers can pick it up.
    /// </summary>
    /// <param name="requestKey">The key whose lock is released; must match the acquired key.</param>
    /// <param name="resultUri">The AT-URI (<c>at://…</c>) of the produced result, or <see langword="null"/> when the request produced no result (for example, it failed).</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the lock has been released and any result published.</returns>
    Task ReleaseAsync( string requestKey, string? resultUri, CancellationToken cancellationToken = default );

    /// <summary>
    /// Waits for the in-flight processor of a request key to publish a result.
    /// </summary>
    /// <param name="requestKey">The key to wait on.</param>
    /// <param name="timeout">The maximum time to wait for a published result.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the published result AT-URI (<c>at://…</c>), or <see langword="null"/>
    /// when the request failed, the wait times out, or the in-flight lock is already gone (no
    /// processor to wait for).
    /// </returns>
    Task<string?> WaitForCompletionAsync( string requestKey, TimeSpan timeout, CancellationToken cancellationToken = default );

    /// <summary>
    /// Waits for the next completion notification for a request key that may have already produced
    /// a partial result (its in-flight lock may already be released).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="WaitForCompletionAsync"/>, a missing in-flight lock is NOT treated as
    /// completion. To close the gap between the caller reading state and subscribing,
    /// <paramref name="missedResultCheck"/> is invoked AFTER the subscription becomes active; a
    /// non-empty value it returns is used as the result without waiting further.
    /// </remarks>
    /// <param name="requestKey">The key to wait on.</param>
    /// <param name="timeout">The maximum time to wait for the next notification.</param>
    /// <param name="missedResultCheck">
    /// An optional callback that re-checks durable state (for example, the saga's final result
    /// URI) for a result published before the subscription was active.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the next non-empty published result AT-URI (<c>at://…</c>), or
    /// <see langword="null"/> when the wait times out and the fallback (if any) found nothing.
    /// </returns>
    Task<string?> WaitForFinalCompletionAsync(
        string requestKey,
        TimeSpan timeout,
        Func<Task<string?>>? missedResultCheck = null,
        CancellationToken cancellationToken = default );
}
