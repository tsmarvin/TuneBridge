using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Durable state store for multi-provider lookup sagas: create-or-get, read, per-provider and
/// per-field updates, the pending-saga index, reconciliation queries, a once-only secondary
/// fan-out guard, and deterministic saga-id derivation.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>RedisSagaStateManager</c>
/// (<c>Infrastructure/Queue/RedisSagaStateManager.cs</c>). Tracks lookup progress across
/// providers; per-provider states are stored separately for efficient partial updates. The saga
/// id is deterministically derived from the lookup key (see <see cref="GenerateSagaId"/>), so
/// identical lookups always map to the same saga. Saga state is the <see cref="LookupSagaState"/>
/// record; result pointers are AT-URIs (<c>at://…</c>).
/// </remarks>
public interface ISagaStateManager {
    /// <summary>
    /// Returns the existing saga for the id, or creates one with the supplied seed values if it
    /// does not yet exist. Idempotent: repeated calls with the same id return the same saga.
    /// </summary>
    /// <param name="sagaId">The saga id, typically produced by <see cref="GenerateSagaId"/> from the lookup key.</param>
    /// <param name="lookupKey">The normalized deduplication key this saga resolves (for example <c>isrc:USRC12345678</c>).</param>
    /// <param name="lookupType">The lookup strategy this saga uses.</param>
    /// <param name="lookupValue">The raw lookup value being resolved.</param>
    /// <param name="originPriority">
    /// The priority the originating request arrived on. When provided, it is recorded as the
    /// saga's origin priority only if one has not been recorded yet (first writer wins). When
    /// <see langword="null"/>, no origin priority is written and the saga defaults to
    /// <see cref="QueuePriority.Background"/> until a worker records one.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the existing or newly created <see cref="LookupSagaState"/>.</returns>
    Task<LookupSagaState> GetOrCreateAsync(
        string sagaId,
        string lookupKey,
        LookupRequestType lookupType,
        string lookupValue,
        QueuePriority? originPriority = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads a saga's current state by id. Used by workers processing a queued request that
    /// references a saga id.
    /// </summary>
    /// <param name="sagaId">The saga id to read.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the <see cref="LookupSagaState"/>, or <see langword="null"/> when no
    /// saga exists with the given id or it has expired.
    /// </returns>
    Task<LookupSagaState?> GetAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Records the outcome of one provider's leg of the saga when a provider lookup completes.
    /// Each provider's state is stored separately, and the saga's TTL is extended to prevent
    /// expiration during active processing.
    /// </summary>
    /// <param name="sagaId">The saga id to update.</param>
    /// <param name="state">The per-provider <see cref="ProviderLookupState"/> to store for this leg.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the provider state has been stored.</returns>
    Task UpdateProviderStateAsync(
        string sagaId,
        ProviderLookupState state,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records the AT-URI of the saga's partial (in-progress) result record: the first ATProto
    /// write, holding results from providers that completed before a rate limit, so users can see
    /// available data while the remaining providers finish.
    /// </summary>
    /// <param name="sagaId">The saga id to update.</param>
    /// <param name="uri">The AT-URI (<c>at://…</c>) of the partial result record.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the partial result URI has been stored.</returns>
    Task SetPartialResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records the AT-URI of the saga's final (completed) result record: the final write, which
    /// replaces any partial result. The saga is fully complete once this URI is set.
    /// </summary>
    /// <param name="sagaId">The saga id to update.</param>
    /// <param name="uri">The AT-URI (<c>at://…</c>) of the final result record.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the final result URI has been stored.</returns>
    Task SetFinalResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a saga and all its associated provider states, used for cleanup after the final
    /// result is written and cached, or for manual intervention (for example removing a stuck
    /// saga).
    /// </summary>
    /// <param name="sagaId">The saga id to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when a saga was deleted, or
    /// <see langword="false"/> when no saga existed with the given id.
    /// </returns>
    Task<bool> DeleteAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Finds sagas whose provider legs are all complete but which were never finalized (no final
    /// result URI set), for a reconciliation sweep that finishes them. Used by the saga
    /// coordinator's polling loop as a Pub/Sub fallback.
    /// </summary>
    /// <param name="minimumAge">Only sagas at least this old are returned, to avoid racing in-flight Pub/Sub messages.</param>
    /// <param name="limit">The maximum number of sagas to return per call; defaults to 100.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the completed-but-unfinalized sagas; an empty list when none qualify.
    /// </returns>
    Task<IReadOnlyList<LookupSagaState>> GetCompletedButUnfinalizedAsync(
        TimeSpan minimumAge,
        int limit = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Adds a saga to the pending-saga index when it is created, so reconciliation sweeps can find
    /// it without scanning the keyspace.
    /// </summary>
    /// <param name="sagaId">The saga id to add to the pending index.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the saga has been added to the index.</returns>
    Task AddToPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Removes a saga from the pending-saga index, typically once it has been finalized or
    /// deleted.
    /// </summary>
    /// <param name="sagaId">The saga id to remove from the pending index.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the saga has been removed from the index.</returns>
    Task RemoveFromPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Sets whether the saga's current result is partial (some providers still pending or
    /// rate-limited).
    /// </summary>
    /// <param name="sagaId">The saga id to update.</param>
    /// <param name="isPartial"><see langword="true"/> when the result is still partial; <see langword="false"/> when complete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the flag has been stored.</returns>
    Task SetIsPartialAsync( string sagaId, bool isPartial, CancellationToken cancellationToken = default );

    /// <summary>
    /// Records which provider seeded the saga (the provider the originating lookup came from).
    /// </summary>
    /// <param name="sagaId">The saga id to update.</param>
    /// <param name="provider">The initial provider for the saga.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the initial provider has been stored.</returns>
    Task SetInitialProviderAsync( string sagaId, SupportedProviders provider, CancellationToken cancellationToken = default );

    /// <summary>
    /// Records the per-provider rate-limit information currently affecting the saga, for user
    /// notification.
    /// </summary>
    /// <param name="sagaId">The saga id to update.</param>
    /// <param name="rateLimitInfo">The per-provider <see cref="ProviderRateLimitInfo"/> entries to store.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the rate-limit information has been stored.</returns>
    Task SetRateLimitInfoAsync( string sagaId, List<ProviderRateLimitInfo> rateLimitInfo, CancellationToken cancellationToken = default );

    /// <summary>
    /// Atomically marks the saga's secondary lookups as queued, succeeding only once (set-if-not-
    /// exists) so the fan-out to secondary providers happens a single time even when both the
    /// <c>saga:completed</c> and <c>complete:{key}</c> events arrive.
    /// </summary>
    /// <param name="sagaId">The saga id to mark.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when this caller set the marker (and should
    /// queue secondaries), or <see langword="false"/> when it was already set by an earlier caller.
    /// </returns>
    Task<bool> TryMarkSecondariesQueuedAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Atomically claims the exclusive right to finalize a saga, succeeding only once
    /// (set-if-not-exists) so exactly one of the overlapping finalization triggers
    /// (<c>saga:completed</c>, <c>complete:{key}</c>, the polling sweep) performs the PDS write.
    /// </summary>
    /// <param name="sagaId">The saga id to claim finalization for.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when this caller acquired the claim (and must
    /// finalize), or <see langword="false"/> when another caller already holds it.
    /// </returns>
    Task<bool> TryClaimFinalizeAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Releases a finalize claim previously acquired via <see cref="TryClaimFinalizeAsync"/>,
    /// so a legitimate retry can re-finalize after a failed PDS write. Must be called only on
    /// the failure path; a saga that finalized successfully keeps its claim until TTL expiry.
    /// </summary>
    /// <param name="sagaId">The saga id whose finalize claim should be released.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the claim has been released.</returns>
    Task ReleaseFinalizeClaimAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>
    /// Seeds the saga with an initial, not-yet-complete provider state for each of the given
    /// providers at the start of a saga, so the set of providers that must complete is known.
    /// </summary>
    /// <param name="sagaId">The saga id to initialize.</param>
    /// <param name="providers">The set of enabled providers to register provider states for.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the provider states have been initialized.</returns>
    Task InitializeProviderStatesAsync( string sagaId, IEnumerable<SupportedProviders> providers, CancellationToken cancellationToken = default );

    /// <summary>
    /// Derives a deterministic saga id from a lookup key. The key is upper-cased, hashed with
    /// SHA-256, and the first 16 bytes of the hash are returned as lowercase hexadecimal.
    /// </summary>
    /// <param name="lookupKey">The lookup key to derive an id from (for example <c>isrc:USRC12345678</c>); must not be null or whitespace.</param>
    /// <returns>A 32-character lowercase hex saga id.</returns>
    /// <remarks>
    /// Because the key is upper-cased before hashing, the saga id is case-insensitive in the
    /// lookup key: two inputs differing only by case collapse to the same saga id. The id is also
    /// stable across calls and processes for the same key.
    /// </remarks>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="lookupKey"/> is null, empty, or whitespace.</exception>
    static string GenerateSagaId( string lookupKey ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupKey );
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes( lookupKey.ToUpperInvariant( ) )
        );
        // Use first 16 bytes for a shorter but still unique ID
        return Convert.ToHexString( hash.AsSpan( 0, 16 ) ).ToLowerInvariant( );
    }
}
