using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Manages the state of multi-provider lookup sagas.
/// </summary>
/// <remarks>
/// Tracks lookup progress across multiple providers. Saga ID is deterministically derived
/// from the lookup key. Provider states are stored separately for efficient partial updates.
/// </remarks>
public interface ISagaStateManager {
    /// <summary>
    /// Gets an existing saga by ID, or creates a new one if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// Saga ID must be deterministically derived from the lookup key so identical lookups
    /// always map to the same saga. Returns the existing saga if one already exists (idempotent).
    /// </remarks>
    /// <param name="sagaId">Unique saga identifier, derived from lookup key.</param>
    /// <param name="lookupKey">The deduplication key (e.g., "isrc:USRC12345678").</param>
    /// <param name="lookupType">Type of lookup being performed.</param>
    /// <param name="lookupValue">The raw lookup value.</param>
    /// <param name="originPriority">
    /// Priority of the originating request. When provided, it is recorded as the saga's
    /// origin priority if one has not been recorded yet (first writer wins). When null,
    /// no origin priority is written and the saga defaults to
    /// <see cref="QueuePriority.Background"/> until a worker records one.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The saga state, either existing or newly created.</returns>
    Task<LookupSagaState> GetOrCreateAsync(
        string sagaId,
        string lookupKey,
        LookupRequestType lookupType,
        string lookupValue,
        QueuePriority? originPriority = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets a saga by its ID.
    /// </summary>
    /// <remarks>
    /// Used by workers when processing a queued request that references a saga ID.
    /// Returns null if the saga does not exist or has expired.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The saga state, or null if not found.</returns>
    Task<LookupSagaState?> GetAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Updates the state for a specific provider within a saga.
    /// </summary>
    /// <remarks>
    /// Called when a provider lookup completes. Each provider's state is stored separately.
    /// Automatically extends the saga TTL to prevent expiration during active processing.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="state">The provider's updated state.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UpdateProviderStateAsync(
        string sagaId,
        ProviderLookupState state,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records the ATProto URI for a partial result.
    /// </summary>
    /// <remarks>
    /// The first ATProto write, containing results from providers that completed before a rate limit.
    /// Allows users to see available data while remaining providers complete.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="uri">The ATProto record URI for the partial result.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetPartialResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records the ATProto URI for the final, complete result.
    /// </summary>
    /// <remarks>
    /// The final ATProto write (or only write if no rate limits were encountered).
    /// Replaces any partial result; the saga is fully complete when this URI is set.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="uri">The ATProto record URI for the final result.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetFinalResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a saga and all its associated provider states.
    /// </summary>
    /// <remarks>
    /// Used for cleanup after the final result is written and cached,
    /// or for manual intervention (e.g., removing stuck sagas).
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the saga was deleted; false if it did not exist.</returns>
    Task<bool> DeleteAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Gets sagas that are complete (all providers finished) but have not been finalized
    /// (no FinalResultUri set).
    /// </summary>
    /// <remarks>
    /// Used by the SagaCoordinator polling loop as a Pub/Sub fallback. Only returns sagas older
    /// than <paramref name="minimumAge"/> to avoid races with in-flight Pub/Sub messages.
    /// </remarks>
    /// <param name="minimumAge">Minimum age of sagas to return (prevents race with Pub/Sub).</param>
    /// <param name="limit">Maximum number of sagas to return per call.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of sagas that are complete but not finalized.</returns>
    Task<IReadOnlyList<LookupSagaState>> GetCompletedButUnfinalizedAsync(
        TimeSpan minimumAge,
        int limit = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Adds a saga to the pending index for efficient polling.
    /// </summary>
    /// <remarks>
    /// Called when a saga is created to track it in the pending set.
    /// This enables O(1) lookup during polling instead of SCAN operations.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task AddToPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Removes a saga from the pending index.
    /// </summary>
    /// <remarks>
    /// Called when a saga is finalized or deleted to remove it from the pending set.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RemoveFromPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Marks a saga as having partial results (some providers rate-limited).
    /// </summary>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="isPartial">Whether the saga has partial results.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetIsPartialAsync( string sagaId, bool isPartial, CancellationToken cancellationToken = default );

    /// <summary>
    /// Sets the initial provider that triggered this lookup.
    /// </summary>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="provider">The provider that initiated the lookup.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetInitialProviderAsync( string sagaId, SupportedProviders provider, CancellationToken cancellationToken = default );

    /// <summary>
    /// Records rate limit information for user notification.
    /// </summary>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="rateLimitInfo">List of rate-limited provider information.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetRateLimitInfoAsync( string sagaId, List<ProviderRateLimitInfo> rateLimitInfo, CancellationToken cancellationToken = default );

    /// <summary>
    /// Atomically marks a saga as having had its secondary lookups queued.
    /// </summary>
    /// <remarks>
    /// Set-if-not-exists so exactly one caller wins the right to queue secondaries,
    /// preventing double-queuing when both <c>saga:completed</c> and <c>complete:{key}</c> events arrive.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if this caller set the marker (and should queue secondaries); false if it was already set.</returns>
    Task<bool> TryMarkSecondariesQueuedAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Initializes provider states for all enabled providers at the start of a saga.
    /// </summary>
    /// <remarks>
    /// Called when creating a saga to mark all providers as pending.
    /// This ensures we know which providers need to complete.
    /// </remarks>
    /// <param name="sagaId">The saga identifier.</param>
    /// <param name="providers">The set of enabled providers to initialize.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InitializeProviderStatesAsync( string sagaId, IEnumerable<SupportedProviders> providers, CancellationToken cancellationToken = default );

    /// <summary>
    /// Generates a deterministic saga ID from a lookup key.
    /// </summary>
    /// <remarks>
    /// The saga ID is derived by hashing the lookup key to create a consistent,
    /// URL-safe identifier that ensures identical lookups map to the same saga.
    /// </remarks>
    /// <param name="lookupKey">The lookup key (e.g., "isrc:USRC12345678").</param>
    /// <returns>A deterministic saga ID.</returns>
    static string GenerateSagaId( string lookupKey ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupKey );
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes( lookupKey.ToUpperInvariant( ) )
        );
        // Use first 16 bytes for a shorter but still unique ID
        return Convert.ToHexString( hash.AsSpan( 0, 16 ) ).ToLowerInvariant( );
    }
}
