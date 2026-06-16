using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Redis-backed implementation of <see cref="ISagaStateManager"/> that stores lookup saga state
/// with separate keys for provider states to enable efficient partial updates: a core hash per
/// saga, a hash per provider, and a pending-saga index.
/// </summary>
/// <param name="redis">Redis connection used for all saga operations.</param>
/// <param name="logger">Logger for saga diagnostics.</param>
/// <param name="settings">Queue configuration, used for the per-write TTL.</param>
/// <remarks>
/// A saga's core fields live in hash <c>saga:{sagaId}</c>; each provider's progress lives in
/// <c>saga:{sagaId}:provider:{provider}</c>; sagas awaiting finalization are tracked in the set
/// <c>saga:pending</c>. Every write refreshes the saga's TTL to the configured job-expiration
/// window, so a stalled saga self-expires and can be retried. Saga completeness is not stored: it
/// is computed from the per-provider hashes at read time, so a saga reads as complete when all of
/// its initialized providers report complete. Redis here is transport and working state, not the
/// system of record — final results live in the ATProto PDS, referenced by the URIs stored here.
/// </remarks>
public sealed partial class RedisSagaStateManager(
    IConnectionMultiplexer redis,
    ILogger<RedisSagaStateManager> logger,
    IOptions<QueueSettings> settings
    ) : ISagaStateManager {

    /// <summary>Redis connection used for all saga operations.</summary>
    private readonly IConnectionMultiplexer _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );

    /// <summary>Logger for saga diagnostics.</summary>
    private readonly ILogger<RedisSagaStateManager> _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );

    /// <summary>Queue configuration, consulted for the per-write job-expiration TTL.</summary>
    private readonly QueueSettings _settings = settings?.Value ?? throw new ArgumentNullException( nameof( settings ) );

    /// <summary>Key prefix for saga hashes. Literal value: <c>"saga:"</c>.</summary>
    private const string SagaPrefix = "saga:";

    /// <summary>Infix between saga id and provider in a provider key. Literal value: <c>":provider:"</c>.</summary>
    private const string ProviderSuffix = ":provider:";

    /// <summary>Key of the set indexing sagas awaiting finalization. Literal value: <c>"saga:pending"</c>.</summary>
    private const string PendingSagaSetKey = "saga:pending";

    // Hash field names for saga state

    /// <summary>Core hash field: the lookup key. Literal: <c>"lookupKey"</c>.</summary>
    private const string FieldLookupKey = "lookupKey";

    /// <summary>Core hash field: the lookup type. Literal: <c>"lookupType"</c>.</summary>
    private const string FieldLookupType = "lookupType";

    /// <summary>Core hash field: the lookup value. Literal: <c>"lookupValue"</c>.</summary>
    private const string FieldLookupValue = "lookupValue";

    /// <summary>Core hash field: saga creation time (ISO-8601). Literal: <c>"createdAt"</c>.</summary>
    private const string FieldCreatedAt = "createdAt";

    /// <summary>Core hash field: URI of the partial result written while awaiting secondaries. Literal: <c>"partialResultUri"</c>.</summary>
    private const string FieldPartialResultUri = "partialResultUri";

    /// <summary>Core hash field: URI of the final result. Literal: <c>"finalResultUri"</c>.</summary>
    private const string FieldFinalResultUri = "finalResultUri";

    /// <summary>Core hash field: whether the current result is partial. Literal: <c>"isPartial"</c>.</summary>
    private const string FieldIsPartial = "isPartial";

    /// <summary>Core hash field: the provider that first resolved the lookup. Literal: <c>"initialProvider"</c>.</summary>
    private const string FieldInitialProvider = "initialProvider";

    /// <summary>Core hash field: serialized per-provider rate-limit info. Literal: <c>"rateLimitInfo"</c>.</summary>
    private const string FieldRateLimitInfo = "rateLimitInfo";

    /// <summary>Core hash field: the priority the lookup originated at. Literal: <c>"originPriority"</c>.</summary>
    private const string FieldOriginPriority = "originPriority";

    /// <summary>Core hash field: single-winner marker that secondary lookups were queued. Literal: <c>"secondariesQueued"</c>.</summary>
    private const string FieldSecondariesQueued = "secondariesQueued";

    /// <summary>Core hash field: single-winner marker that finalization has been claimed. Literal: <c>"finalizeClaimed"</c>.</summary>
    private const string FieldFinalizeClaimed = "finalizeClaimed";

    /// <summary>Core hash field: the highest provider-count already durably written to the PDS. Literal: <c>"writeGeneration"</c>.</summary>
    private const string FieldWriteGeneration = "writeGeneration";

    // Hash field names for provider state

    /// <summary>Provider hash field: whether the provider has finished. Literal: <c>"isComplete"</c>.</summary>
    private const string FieldIsComplete = "isComplete";

    /// <summary>Provider hash field: whether the provider succeeded. Literal: <c>"isSuccess"</c>.</summary>
    private const string FieldIsSuccess = "isSuccess";

    /// <summary>Provider hash field: serialized provider result. Literal: <c>"resultJson"</c>.</summary>
    private const string FieldResultJson = "resultJson";

    /// <summary>Provider hash field: when the provider completed (ISO-8601). Literal: <c>"completedAt"</c>.</summary>
    private const string FieldCompletedAt = "completedAt";

    /// <summary>Provider hash field: an error message if the provider failed. Literal: <c>"errorMessage"</c>.</summary>
    private const string FieldErrorMessage = "errorMessage";

    /// <summary>
    /// Returns the existing saga for an id, refreshing its TTL, or creates a new one.
    /// </summary>
    /// <param name="sagaId">The deterministic saga id for the lookup.</param>
    /// <param name="lookupKey">The lookup key the saga tracks.</param>
    /// <param name="lookupType">The lookup type.</param>
    /// <param name="lookupValue">The lookup value.</param>
    /// <param name="originPriority">
    /// The priority the lookup originated at. On an existing saga this is set only if not already
    /// present (so the original origin survives a later background re-enqueue); on a new saga it is
    /// stored, defaulting to background when omitted.
    /// </param>
    /// <param name="cancellationToken">Token forwarded to nested reads.</param>
    /// <returns>The existing or newly created <see cref="LookupSagaState"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/>, <paramref name="lookupKey"/>, or <paramref name="lookupValue"/> is null or whitespace.</exception>
    public async Task<LookupSagaState> GetOrCreateAsync(
        string sagaId,
        string lookupKey,
        LookupRequestType lookupType,
        string lookupValue,
        QueuePriority? originPriority = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupKey );
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupValue );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        // Check if saga already exists
        bool exists = await db.KeyExistsAsync( key );

        if (exists) {
            // Extend TTL and return existing state
            _ = await db.KeyExpireAsync( key, ttl );

            // Record the origin priority if none has been recorded yet (first writer wins).
            // Sagas created by the orchestrator before a worker processes the first request
            // have no origin priority field; the first request's priority becomes the origin.
            if (originPriority.HasValue &&
                await db.HashSetAsync( key, FieldOriginPriority, originPriority.Value.ToString( ), When.NotExists )) {
                LogOriginPrioritySet( _logger, originPriority.Value, sagaId );
            }

            LookupSagaState? existingState = await GetAsync( sagaId, cancellationToken );

            if (existingState is not null) {
                LogSagaResumed( _logger, sagaId );
                return existingState;
            }
        }

        // Create new saga. finalResultUri is intentionally not pre-populated so the
        // When.NotExists guard in SetFinalResultUriAsync can enforce first-writer-wins.
        List<HashEntry> sagaHashEntries = [
            new HashEntry( FieldLookupKey, lookupKey ),
            new HashEntry( FieldLookupType, lookupType.ToString() ),
            new HashEntry( FieldLookupValue, lookupValue ),
            new HashEntry( FieldCreatedAt, DateTimeOffset.UtcNow.ToString( "O" ) ),
            new HashEntry( FieldPartialResultUri, RedisValue.EmptyString ),
        ];

        if (originPriority.HasValue) {
            sagaHashEntries.Add( new HashEntry( FieldOriginPriority, originPriority.Value.ToString( ) ) );
        }

        await db.HashSetAsync( key, [.. sagaHashEntries] );
        _ = await db.KeyExpireAsync( key, ttl );

        // Add to pending index for efficient polling
        await AddToPendingIndexAsync( sagaId, cancellationToken );

        LogSagaCreated( _logger, sagaId, lookupType, lookupValue );

        return new LookupSagaState {
            SagaId = sagaId,
            LookupKey = lookupKey,
            LookupType = lookupType,
            LookupValue = lookupValue,
            CreatedAt = DateTimeOffset.UtcNow,
            ProviderStates = [],
            PartialResultUri = null,
            FinalResultUri = null,
            OriginPriority = originPriority ?? QueuePriority.Background
        };
    }

    /// <summary>
    /// Reads a saga's full state, including each provider's progress.
    /// </summary>
    /// <param name="sagaId">The saga id to read.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>
    /// The reconstructed <see cref="LookupSagaState"/>, or null if the saga hash is absent or its
    /// required core fields are missing or invalid.
    /// </returns>
    /// <remarks>
    /// Origin priority defaults to background when the stored value is missing or unparseable. The
    /// returned state's completeness is derived from the loaded provider states, not read from a
    /// stored flag.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task<LookupSagaState?> GetAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );

        HashEntry[] sagaHash = await db.HashGetAllAsync( key );

        if (sagaHash.Length == 0) {
            return null;
        }

        Dictionary<string, string> fields = sagaHash.ToDictionary(
            h => h.Name.ToString( ),
            h => h.Value.ToString( )
        );

        // Parse core saga state
        if (!fields.TryGetValue( FieldLookupKey, out string? lookupKey ) ||
            !fields.TryGetValue( FieldLookupType, out string? lookupTypeStr ) ||
            !fields.TryGetValue( FieldLookupValue, out string? lookupValue )) {
            LogIncompleteCoreState( _logger, sagaId );
            return null;
        }

        if (!Enum.TryParse<LookupRequestType>( lookupTypeStr, out LookupRequestType lookupType )) {
            LogInvalidLookupType( _logger, sagaId, lookupTypeStr );
            return null;
        }

        _ = fields.TryGetValue( FieldCreatedAt, out string? createdAtStr );
        _ = fields.TryGetValue( FieldPartialResultUri, out string? partialUri );
        _ = fields.TryGetValue( FieldFinalResultUri, out string? finalUri );
        _ = fields.TryGetValue( FieldIsPartial, out string? isPartialStr );
        _ = fields.TryGetValue( FieldInitialProvider, out string? initialProviderStr );
        _ = fields.TryGetValue( FieldRateLimitInfo, out string? rateLimitInfoJson );
        _ = fields.TryGetValue( FieldOriginPriority, out string? originPriorityStr );
        _ = fields.TryGetValue( FieldWriteGeneration, out string? writeGenerationStr );

        DateTimeOffset createdAt = !string.IsNullOrEmpty( createdAtStr )
            ? DateTimeOffset.Parse( createdAtStr )
            : DateTimeOffset.UtcNow;

        _ = bool.TryParse( isPartialStr, out bool isPartial );

        SupportedProviders? initialProvider = !string.IsNullOrEmpty( initialProviderStr ) &&
            Enum.TryParse<SupportedProviders>( initialProviderStr, out SupportedProviders parsedProvider )
                ? parsedProvider
                : null;

        List<ProviderRateLimitInfo>? rateLimitInfo = !string.IsNullOrEmpty( rateLimitInfoJson )
            ? System.Text.Json.JsonSerializer.Deserialize<List<ProviderRateLimitInfo>>( rateLimitInfoJson )
            : null;

        // Missing field (sagas persisted before this field existed) defaults to Background
        // so old sagas are never promoted to interactive.
        QueuePriority originPriority = !string.IsNullOrEmpty( originPriorityStr ) &&
            Enum.TryParse<QueuePriority>( originPriorityStr, out QueuePriority parsedPriority )
                ? parsedPriority
                : QueuePriority.Background;

        // Pre-existing sagas that have no writeGeneration field default to 0, so the first
        // write against them advances normally.
        _ = int.TryParse( writeGenerationStr, out int writeGeneration );

        // Load provider states
        Dictionary<SupportedProviders, ProviderLookupState> providerStates = await LoadProviderStatesAsync( db, sagaId );

        return new LookupSagaState {
            SagaId = sagaId,
            LookupKey = lookupKey,
            LookupType = lookupType,
            LookupValue = lookupValue,
            CreatedAt = createdAt,
            ProviderStates = providerStates,
            PartialResultUri = string.IsNullOrEmpty( partialUri ) ? null : partialUri,
            FinalResultUri = string.IsNullOrEmpty( finalUri ) ? null : finalUri,
            IsPartial = isPartial,
            InitialProvider = initialProvider,
            RateLimitInfo = rateLimitInfo,
            OriginPriority = originPriority,
            WriteGeneration = writeGeneration
        };
    }

    /// <summary>
    /// Writes one provider's progress into its provider hash and refreshes both the provider and
    /// saga TTLs.
    /// </summary>
    /// <param name="sagaId">The saga the provider belongs to.</param>
    /// <param name="state">The provider state to persist.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the provider hash is written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    public async Task UpdateProviderStateAsync(
        string sagaId,
        ProviderLookupState state,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentNullException.ThrowIfNull( state );

        string key = GetProviderKey( sagaId, state.Provider );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        HashEntry[] providerHash = [
            new HashEntry( FieldIsComplete, state.IsComplete.ToString() ),
            new HashEntry( FieldIsSuccess, state.IsSuccess.ToString() ),
            new HashEntry( FieldResultJson, state.ResultJson ?? string.Empty ),
            new HashEntry( FieldCompletedAt, state.CompletedAt?.ToString( "O" ) ?? string.Empty ),
            new HashEntry( FieldErrorMessage, state.ErrorMessage ?? string.Empty )
        ];

        await db.HashSetAsync( key, providerHash );
        _ = await db.KeyExpireAsync( key, ttl );

        // Also extend the main saga key TTL
        _ = await db.KeyExpireAsync( GetSagaKey( sagaId ), ttl );

        LogProviderStateUpdated( _logger, sagaId, state.Provider, state.IsComplete, state.IsSuccess );
    }

    /// <summary>
    /// Stores the URI of a partial result written while the saga awaits remaining providers.
    /// </summary>
    /// <param name="sagaId">The saga to update.</param>
    /// <param name="uri">The partial-result URI.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the field is written and the TTL refreshed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> or <paramref name="uri"/> is null or whitespace.</exception>
    public async Task SetPartialResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( uri );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldPartialResultUri, uri );
        _ = await db.KeyExpireAsync( key, ttl );

        LogPartialResultUriSet( _logger, sagaId, uri );
    }

    /// <summary>
    /// Stores the URI of the final result and removes the saga from the pending index.
    /// </summary>
    /// <param name="sagaId">The saga to finalize.</param>
    /// <param name="uri">The final-result URI (a PDS record URI).</param>
    /// <param name="cancellationToken">Token forwarded to the pending-index removal.</param>
    /// <returns>A task that completes once the field is written and the saga de-indexed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> or <paramref name="uri"/> is null or whitespace.</exception>
    public async Task SetFinalResultUriAsync(
        string sagaId,
        string uri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( uri );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldFinalResultUri, uri, When.NotExists );
        _ = await db.KeyExpireAsync( key, ttl );

        // Remove from pending index since saga is now finalized
        await RemoveFromPendingIndexAsync( sagaId, cancellationToken );

        LogFinalResultUriSet( _logger, sagaId, uri );
    }

    /// <summary>
    /// Deletes a saga: removes it from the pending index, deletes its core hash, and deletes every
    /// provider hash.
    /// </summary>
    /// <param name="sagaId">The saga to delete.</param>
    /// <param name="cancellationToken">Token forwarded to the pending-index removal.</param>
    /// <returns>True if the core saga hash existed and was deleted; otherwise false.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task<bool> DeleteAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        IDatabase db = _redis.GetDatabase( );

        // Remove from pending index
        await RemoveFromPendingIndexAsync( sagaId, cancellationToken );

        // Delete main saga key
        string sagaKey = GetSagaKey( sagaId );
        bool sagaDeleted = await db.KeyDeleteAsync( sagaKey );

        // Delete all provider state keys
        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            string providerKey = GetProviderKey( sagaId, provider );
            _ = await db.KeyDeleteAsync( providerKey );
        }

        if (sagaDeleted) {
            LogSagaDeleted( _logger, sagaId );
        }

        return sagaDeleted;
    }

    /// <summary>
    /// Sets the saga's partial-result flag.
    /// </summary>
    /// <param name="sagaId">The saga to update.</param>
    /// <param name="isPartial">Whether the current result is partial.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the field is written and the TTL refreshed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task SetIsPartialAsync( string sagaId, bool isPartial, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldIsPartial, isPartial.ToString( ) );
        _ = await db.KeyExpireAsync( key, ttl );

        LogIsPartialSet( _logger, isPartial, sagaId );
    }

    /// <summary>
    /// Records which provider first resolved the lookup.
    /// </summary>
    /// <param name="sagaId">The saga to update.</param>
    /// <param name="provider">The initial provider.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the field is written and the TTL refreshed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task SetInitialProviderAsync( string sagaId, SupportedProviders provider, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashSetAsync( key, FieldInitialProvider, provider.ToString( ) );
        _ = await db.KeyExpireAsync( key, ttl );

        LogInitialProviderSet( _logger, provider, sagaId );
    }

    /// <summary>
    /// Stores the saga's per-provider rate-limit information as JSON.
    /// </summary>
    /// <param name="sagaId">The saga to update.</param>
    /// <param name="rateLimitInfo">The rate-limit entries to persist.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the field is written and the TTL refreshed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rateLimitInfo"/> is null.</exception>
    public async Task SetRateLimitInfoAsync( string sagaId, List<ProviderRateLimitInfo> rateLimitInfo, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentNullException.ThrowIfNull( rateLimitInfo );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        string json = System.Text.Json.JsonSerializer.Serialize( rateLimitInfo );
        _ = await db.HashSetAsync( key, FieldRateLimitInfo, json );
        _ = await db.KeyExpireAsync( key, ttl );

        LogRateLimitInfoSet( _logger, sagaId, rateLimitInfo.Count );
    }

    /// <summary>
    /// Atomically claims the right to queue secondary lookups for a saga, so only one concurrent
    /// handler fans them out.
    /// </summary>
    /// <param name="sagaId">The saga to mark.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>True if this caller set the marker (and should queue secondaries); false if another caller already did.</returns>
    /// <remarks>Uses a hash set with "when not exists" semantics (HSETNX) so exactly one caller wins.</remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task<bool> TryMarkSecondariesQueuedAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        // HSETNX: only the first caller creates the field, making the
        // queue-secondaries decision atomic across racing coordinator handlers.
        bool acquired = await db.HashSetAsync( key, FieldSecondariesQueued, true.ToString( ), When.NotExists );
        _ = await db.KeyExpireAsync( key, ttl );

        LogSecondariesQueuedMarker( _logger, sagaId, acquired );

        return acquired;
    }

    /// <summary>
    /// Atomically claims the exclusive right to finalize a saga, so exactly one concurrent
    /// trigger performs the PDS write.
    /// </summary>
    /// <param name="sagaId">The saga to claim finalization for.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>True if this caller won the claim (and must finalize); false if another caller already holds it.</returns>
    /// <remarks>Uses a hash set with "when not exists" semantics (HSETNX) so exactly one caller wins.</remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task<bool> TryClaimFinalizeAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        bool acquired = await db.HashSetAsync( key, FieldFinalizeClaimed, true.ToString( ), When.NotExists );
        _ = await db.KeyExpireAsync( key, ttl );

        LogFinalizeClaimMarker( _logger, sagaId, acquired );

        return acquired;
    }

    /// <summary>
    /// Releases a finalize claim so a legitimate retry can re-finalize after a failed PDS write.
    /// Must only be called on the failure path; successful finalizations leave the claim set until TTL expiry.
    /// </summary>
    /// <param name="sagaId">The saga whose finalize claim should be released.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the claim field has been deleted.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task ReleaseFinalizeClaimAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        _ = await db.HashDeleteAsync( key, FieldFinalizeClaimed );
        _ = await db.KeyExpireAsync( key, ttl );

        LogFinalizeClaimReleased( _logger, sagaId );
    }

    /// <summary>
    /// Atomically advances the write generation to <paramref name="generation"/> only when the
    /// stored generation is strictly less than <paramref name="generation"/>, using a Lua
    /// compare-and-set so the advancement is exactly one winner per generation level even under
    /// concurrent callers. Refreshes the saga TTL after a successful advance.
    /// </summary>
    /// <param name="sagaId">The saga to advance.</param>
    /// <param name="generation">The generation to advance to.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>
    /// <see langword="true"/> when this caller advanced the generation (and must perform the PDS
    /// write); <see langword="false"/> when the stored generation was already at or above
    /// <paramref name="generation"/> and the write should be skipped.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task<bool> TryAdvanceWriteGenerationAsync( string sagaId, int generation, CancellationToken ct = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        RedisResult result = await db.ScriptEvaluateAsync(
            AdvanceWriteGenerationScript,
            keys: [key],
            values: [FieldWriteGeneration, generation]
        );

        bool advanced = (int)result == 1;

        if (advanced) {
            _ = await db.KeyExpireAsync( key, ttl );
            LogWriteGenerationAdvanced( _logger, sagaId, generation );
        }

        return advanced;
    }

    /// <summary>
    /// Lua script that advances the write-generation field only when the stored value is strictly
    /// less than the requested generation. Returns 1 on advance, 0 when the stored value is
    /// already at or above the requested generation.
    /// </summary>
    private const string AdvanceWriteGenerationScript =
        "local cur = tonumber(redis.call('hget', KEYS[1], ARGV[1]) or '0'); " +
        "if cur < tonumber(ARGV[2]) then redis.call('hset', KEYS[1], ARGV[1], ARGV[2]); return 1 " +
        "else return 0 end";

    /// <summary>
    /// Lua script that resets the write-generation field from <c>ARGV[2]</c> back to <c>ARGV[3]</c>
    /// only when the stored value still equals <c>ARGV[2]</c> (the value this caller advanced to).
    /// Returns 1 when the reset took effect, 0 when a concurrent handler has already advanced
    /// beyond the caller's generation and the reset is skipped to avoid clobbering a later write.
    /// </summary>
    private const string ResetWriteGenerationScript =
        "local cur = tonumber(redis.call('hget', KEYS[1], ARGV[1]) or '0'); " +
        "if cur == tonumber(ARGV[2]) then redis.call('hset', KEYS[1], ARGV[1], ARGV[3]); return 1 " +
        "else return 0 end";

    /// <summary>
    /// Conditionally resets the write generation from <paramref name="advancedTo"/> back to
    /// <paramref name="priorGeneration"/> after a non-terminal pre-durability PDS write failure,
    /// so the next retry can re-advance and re-write. The reset only takes effect when the stored
    /// generation still equals <paramref name="advancedTo"/>; if a concurrent handler has already
    /// advanced the generation further, the stored value is left untouched. Refreshes the TTL on
    /// a successful reset.
    /// </summary>
    /// <param name="sagaId">The saga whose write generation should be reset.</param>
    /// <param name="advancedTo">The generation this caller advanced to; the stored value must equal this for the reset to take effect.</param>
    /// <param name="priorGeneration">The generation to restore; typically <c>advancedTo - 1</c>.</param>
    /// <param name="ct">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes when the conditional reset attempt has been made and the TTL optionally refreshed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task ResetWriteGenerationAsync( string sagaId, int advancedTo, int priorGeneration, CancellationToken ct = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        string key = GetSagaKey( sagaId );
        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        RedisResult result = await db.ScriptEvaluateAsync(
            ResetWriteGenerationScript,
            keys: [key],
            values: [FieldWriteGeneration, advancedTo, priorGeneration]
        );

        bool reset = (int)result == 1;

        if (reset) {
            _ = await db.KeyExpireAsync( key, ttl );
        }

        LogWriteGenerationReset( _logger, sagaId, advancedTo, priorGeneration, reset );
    }

    /// <summary>
    /// Initializes each provider's state hash to a not-complete, not-success baseline without
    /// clobbering any state already in flight.
    /// </summary>
    /// <param name="sagaId">The saga whose providers are being initialized.</param>
    /// <param name="providers">The providers to initialize.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once all providers are initialized.</returns>
    /// <remarks>
    /// Each provider hash is written inside a transaction guarded by a key-not-exists condition, so
    /// a re-initialization never overwrites a provider that already has progress; for those, only
    /// the TTL is refreshed.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
    public async Task InitializeProviderStatesAsync( string sagaId, IEnumerable<SupportedProviders> providers, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentNullException.ThrowIfNull( providers );

        IDatabase db = _redis.GetDatabase( );
        TimeSpan ttl = TimeSpan.FromMinutes( _settings.JobExpirationMinutes );

        foreach (SupportedProviders provider in providers) {
            string key = GetProviderKey( sagaId, provider );

            // Idempotent: never overwrite existing provider state. A resumed saga may
            // already have completed or in-progress providers that must be preserved.
            // The KeyNotExists condition makes the pending-state write commit atomically
            // only when no state exists yet, so a concurrent UpdateProviderStateAsync
            // cannot be regressed back to pending.
            HashEntry[] providerHash = [
                new HashEntry( FieldIsComplete, false.ToString() ),
                new HashEntry( FieldIsSuccess, false.ToString() ),
                new HashEntry( FieldResultJson, string.Empty ),
                new HashEntry( FieldCompletedAt, string.Empty ),
                new HashEntry( FieldErrorMessage, string.Empty )
            ];

            ITransaction transaction = db.CreateTransaction( );
            _ = transaction.AddCondition( Condition.KeyNotExists( key ) );
            _ = transaction.HashSetAsync( key, providerHash );
            _ = transaction.KeyExpireAsync( key, ttl );

            if (!await transaction.ExecuteAsync( )) {
                // Provider state already exists — leave it untouched and refresh the TTL.
                _ = await db.KeyExpireAsync( key, ttl );
            }
        }

        if (_logger.IsEnabled( LogLevel.Debug )) {
            int providerCount = providers.Count( );
            LogProviderStatesInitialized( _logger, sagaId, providerCount );
        }
    }

    /// <summary>
    /// Loads the per-provider states for a saga by reading each provider's hash.
    /// </summary>
    /// <param name="db">The Redis database to read from.</param>
    /// <param name="sagaId">The saga whose provider states are loaded.</param>
    /// <returns>A map of provider to its loaded state; providers with no hash are omitted.</returns>
    private static async Task<Dictionary<SupportedProviders, ProviderLookupState>> LoadProviderStatesAsync(
        IDatabase db,
        string sagaId
    ) {
        Dictionary<SupportedProviders, ProviderLookupState> states = [];

        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            string key = GetProviderKey( sagaId, provider );
            HashEntry[] hash = await db.HashGetAllAsync( key );

            if (hash.Length == 0) {
                continue;
            }

            Dictionary<string, string> fields = hash.ToDictionary(
                h => h.Name.ToString( ),
                h => h.Value.ToString( )
            );

            _ = bool.TryParse( fields.GetValueOrDefault( FieldIsComplete, "false" ), out bool isComplete );
            _ = bool.TryParse( fields.GetValueOrDefault( FieldIsSuccess, "false" ), out bool isSuccess );

            string? resultJson = fields.GetValueOrDefault( FieldResultJson );
            string? completedAtStr = fields.GetValueOrDefault( FieldCompletedAt );
            string? errorMessage = fields.GetValueOrDefault( FieldErrorMessage );

            DateTimeOffset? completedAt = !string.IsNullOrEmpty( completedAtStr )
                ? DateTimeOffset.Parse( completedAtStr )
                : null;

            states[provider] = new ProviderLookupState(
                Provider: provider,
                IsComplete: isComplete,
                IsSuccess: isSuccess,
                ResultJson: string.IsNullOrEmpty( resultJson ) ? null : resultJson,
                CompletedAt: completedAt,
                ErrorMessage: string.IsNullOrEmpty( errorMessage ) ? null : errorMessage
            );
        }

        return states;
    }

    /// <summary>
    /// Sweeps the pending index for sagas that have completed but were never finalized, pruning
    /// stale index entries as it goes.
    /// </summary>
    /// <param name="minimumAge">Only sagas older than this are returned, to avoid racing in-flight work.</param>
    /// <param name="limit">Maximum number of sagas to return.</param>
    /// <param name="cancellationToken">Token forwarded to nested reads.</param>
    /// <returns>Completed-but-unfinalized sagas, up to <paramref name="limit"/>.</returns>
    /// <remarks>
    /// This is the coordinator's safety net for completion events dropped by Redis Pub/Sub (which is
    /// at-most-once). Index entries whose saga hash no longer exists, and entries that are already
    /// finalized, are removed from the index during the sweep (self-healing).
    /// </remarks>
    public async Task<IReadOnlyList<LookupSagaState>> GetCompletedButUnfinalizedAsync(
        TimeSpan minimumAge,
        int limit = 100,
        CancellationToken cancellationToken = default
    ) {
        IDatabase db = _redis.GetDatabase( );
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - minimumAge;
        List<LookupSagaState> result = [];

        // Get all saga IDs from the pending set
        RedisValue[] pendingSagaIds = await db.SetMembersAsync( PendingSagaSetKey );

        foreach (RedisValue sagaIdValue in pendingSagaIds) {
            if (result.Count >= limit) {
                break;
            }

            string sagaId = sagaIdValue.ToString( );
            LookupSagaState? saga = await GetAsync( sagaId, cancellationToken );

            if (saga is null) {
                // Saga expired or was deleted - clean up the index
                _ = await db.SetRemoveAsync( PendingSagaSetKey, sagaId );
                continue;
            }

            // Skip if already finalized
            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                // Already finalized - remove from pending index
                _ = await db.SetRemoveAsync( PendingSagaSetKey, sagaId );
                continue;
            }

            // Skip if too young (avoid race with Pub/Sub)
            if (saga.CreatedAt > cutoff) {
                continue;
            }

            // Check if all providers are complete
            if (!saga.IsComplete) {
                continue;
            }

            result.Add( saga );
        }

        if (result.Count > 0) {
            LogUnfinalizedSagasFound( _logger, result.Count );
        }

        return result;
    }

    /// <summary>
    /// Adds a saga to the pending-finalization index.
    /// </summary>
    /// <param name="sagaId">The saga to index.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the saga is added to the index.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task AddToPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.SetAddAsync( PendingSagaSetKey, sagaId );

        LogAddedToPendingIndex( _logger, sagaId );
    }

    /// <summary>
    /// Removes a saga from the pending-finalization index.
    /// </summary>
    /// <param name="sagaId">The saga to de-index.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the saga is removed from the index.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sagaId"/> is null or whitespace.</exception>
    public async Task RemoveFromPendingIndexAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.SetRemoveAsync( PendingSagaSetKey, sagaId );

        LogRemovedFromPendingIndex( _logger, sagaId );
    }

    /// <summary>Builds the core saga hash key: <c>saga:{sagaId}</c>.</summary>
    /// <param name="sagaId">The saga id.</param>
    /// <returns>The core saga key.</returns>
    private static string GetSagaKey( string sagaId ) => $"{SagaPrefix}{sagaId}";

    /// <summary>Builds a provider hash key: <c>saga:{sagaId}:provider:{provider}</c>.</summary>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="provider">The provider.</param>
    /// <returns>The provider key.</returns>
    private static string GetProviderKey( string sagaId, SupportedProviders provider ) =>
        $"{SagaPrefix}{sagaId}{ProviderSuffix}{provider}";

    #region LoggerMessage Methods

    /// <summary>Logs that an existing saga was resumed rather than created.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The resumed saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerResumed,
        Level = LogLevel.Debug,
        Message = "Resumed existing saga {SagaId}" )]
    internal static partial void LogSagaResumed( ILogger logger, string sagaId );

    /// <summary>Logs that a new saga was created.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The new saga id.</param>
    /// <param name="lookupType">The lookup type.</param>
    /// <param name="lookupValue">The lookup value.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerCreated,
        Level = LogLevel.Information,
        Message = "Created new saga {SagaId} for lookup {LookupType}:{LookupValue}" )]
    internal static partial void LogSagaCreated( ILogger logger, string sagaId, LookupRequestType lookupType, string lookupValue );

    /// <summary>Logs that a saga hash was missing required core fields and could not be read.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The affected saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerIncompleteCoreState,
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} has incomplete core state" )]
    internal static partial void LogIncompleteCoreState( ILogger logger, string sagaId );

    /// <summary>Logs that a saga stored an unparseable lookup type.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The affected saga id.</param>
    /// <param name="type">The invalid lookup-type string.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerInvalidLookupType,
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} has invalid lookup type: {Type}" )]
    internal static partial void LogInvalidLookupType( ILogger logger, string sagaId, string type );

    /// <summary>Logs that a provider's state was updated.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="provider">The provider updated.</param>
    /// <param name="complete">Whether the provider is now complete.</param>
    /// <param name="success">Whether the provider succeeded.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerProviderStateUpdated,
        Level = LogLevel.Debug,
        Message = "Updated provider state for saga {SagaId}, provider {Provider}: complete={Complete}, success={Success}" )]
    internal static partial void LogProviderStateUpdated( ILogger logger, string sagaId, SupportedProviders provider, bool complete, bool success );

    /// <summary>Logs that the partial-result URI was set.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="uri">The partial-result URI.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerPartialResultUriSet,
        Level = LogLevel.Information,
        Message = "Set partial result URI for saga {SagaId}: {Uri}" )]
    internal static partial void LogPartialResultUriSet( ILogger logger, string sagaId, string uri );

    /// <summary>Logs that the final-result URI was set.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="uri">The final-result URI.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerFinalResultUriSet,
        Level = LogLevel.Information,
        Message = "Set final result URI for saga {SagaId}: {Uri}" )]
    internal static partial void LogFinalResultUriSet( ILogger logger, string sagaId, string uri );

    /// <summary>Logs that a saga was deleted.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The deleted saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerDeleted,
        Level = LogLevel.Information,
        Message = "Deleted saga {SagaId}" )]
    internal static partial void LogSagaDeleted( ILogger logger, string sagaId );

    /// <summary>Logs that the partial flag was set.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="isPartial">The new partial flag value.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerIsPartialSet,
        Level = LogLevel.Debug,
        Message = "Set isPartial={IsPartial} for saga {SagaId}" )]
    internal static partial void LogIsPartialSet( ILogger logger, bool isPartial, string sagaId );

    /// <summary>Logs that the initial provider was set.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The initial provider.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerInitialProviderSet,
        Level = LogLevel.Debug,
        Message = "Set initialProvider={Provider} for saga {SagaId}" )]
    internal static partial void LogInitialProviderSet( ILogger logger, SupportedProviders provider, string sagaId );

    /// <summary>Logs that rate-limit info was stored.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="count">The number of rate-limited providers recorded.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerRateLimitInfoSet,
        Level = LogLevel.Debug,
        Message = "Set rateLimitInfo for saga {SagaId} with {Count} rate-limited providers" )]
    internal static partial void LogRateLimitInfoSet( ILogger logger, string sagaId, int count );

    /// <summary>Logs that provider states were initialized for a saga.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="count">The number of providers initialized.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerProviderStatesInitialized,
        Level = LogLevel.Debug,
        Message = "Initialized provider states for saga {SagaId} with {Count} providers" )]
    internal static partial void LogProviderStatesInitialized( ILogger logger, string sagaId, int count );

    /// <summary>Logs how many completed-but-unfinalized sagas the polling sweep found.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of sagas found.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerUnfinalizedSagasFound,
        Level = LogLevel.Information,
        Message = "Found {Count} completed but unfinalized sagas during polling" )]
    internal static partial void LogUnfinalizedSagasFound( ILogger logger, int count );

    /// <summary>Logs that a saga was added to the pending index.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The indexed saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerAddedToPendingIndex,
        Level = LogLevel.Debug,
        Message = "Added saga {SagaId} to pending index" )]
    internal static partial void LogAddedToPendingIndex( ILogger logger, string sagaId );

    /// <summary>Logs that a saga was removed from the pending index.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The de-indexed saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerRemovedFromPendingIndex,
        Level = LogLevel.Debug,
        Message = "Removed saga {SagaId} from pending index" )]
    internal static partial void LogRemovedFromPendingIndex( ILogger logger, string sagaId );

    /// <summary>Logs that the origin priority was set on first write.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="priority">The origin priority.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerOriginPrioritySet,
        Level = LogLevel.Debug,
        Message = "Set originPriority={Priority} for saga {SagaId}" )]
    internal static partial void LogOriginPrioritySet( ILogger logger, QueuePriority priority, string sagaId );

    /// <summary>Logs the outcome of the atomic secondaries-queued claim.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="acquired">Whether this caller won the claim.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerSecondariesQueuedMarker,
        Level = LogLevel.Debug,
        Message = "Secondaries-queued marker for saga {SagaId}: acquired={Acquired}" )]
    internal static partial void LogSecondariesQueuedMarker( ILogger logger, string sagaId, bool acquired );

    /// <summary>Logs the outcome of the atomic finalize claim attempt.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="acquired">Whether this caller won the claim.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerFinalizeClaimMarker,
        Level = LogLevel.Debug,
        Message = "Finalize claim for saga {SagaId}: acquired={Acquired}" )]
    internal static partial void LogFinalizeClaimMarker( ILogger logger, string sagaId, bool acquired );

    /// <summary>Logs that a finalize claim was released so a retry can re-finalize.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerFinalizeClaimReleased,
        Level = LogLevel.Debug,
        Message = "Finalize claim released for saga {SagaId}" )]
    internal static partial void LogFinalizeClaimReleased( ILogger logger, string sagaId );

    /// <summary>Logs that the write generation was advanced for a saga.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="generation">The generation advanced to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerWriteGenerationAdvanced,
        Level = LogLevel.Debug,
        Message = "Write generation advanced to {Generation} for saga {SagaId}" )]
    internal static partial void LogWriteGenerationAdvanced( ILogger logger, string sagaId, int generation );

    /// <summary>Logs the outcome of a conditional write-generation reset attempt after a pre-durability failure.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    /// <param name="advancedTo">The generation this caller had advanced to (the expected stored value).</param>
    /// <param name="priorGeneration">The generation to restore.</param>
    /// <param name="reset">Whether the conditional reset succeeded (false means a concurrent handler advanced further).</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisSagaStateManagerWriteGenerationReset,
        Level = LogLevel.Debug,
        Message = "Write generation conditional reset from {AdvancedTo} to {PriorGeneration} for saga {SagaId}: reset={Reset}" )]
    internal static partial void LogWriteGenerationReset( ILogger logger, string sagaId, int advancedTo, int priorGeneration, bool reset );

    #endregion
}
