using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.Services.Queue;
using StackExchange.Redis;

namespace BridgeBeats.Worker.SagaCoordinator;

/// <summary>
/// Background service that coordinates saga completion by:
/// <list type="bullet">
///   <item>Subscribing to Redis Pub/Sub for provider completion events</item>
///   <item>Polling for completed sagas on an interval</item>
///   <item>Assembling final results from completed sagas</item>
///   <item>Writing final results to ATProto and cache</item>
///   <item>Releasing deduplication locks to notify waiters</item>
/// </list>
/// </summary>
public sealed class SagaCoordinatorBackgroundService : BackgroundService {
    private readonly IConnectionMultiplexer _redis;
    private readonly ISagaStateManager _sagaManager;
    private readonly IATProtoStorageService _atProtoStorage;
    private readonly IMediaLinkCacheRepository _cacheRepository;
    private readonly IRequestDeduplicator _deduplicator;
    private readonly SagaResultCombiner _resultCombiner;
    private readonly IProviderQueueResolver<QueuedLookupRequest> _queueResolver;
    private readonly HashSet<SupportedProviders> _enabledProviders;
    private readonly ILogger<SagaCoordinatorBackgroundService> _logger;

    private const string SagaCompletedChannel = "saga:completed";
    private const string LookupCompleteChannelPattern = "complete:*";
    private static readonly TimeSpan s_pollingInterval = TimeSpan.FromSeconds( 30 );
    private static readonly TimeSpan s_minimumSagaAge = TimeSpan.FromSeconds( 15 );
    private const int PollingBatchLimit = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaCoordinatorBackgroundService"/> class.
    /// </summary>
    /// <param name="redis">Redis connection for Pub/Sub subscriptions.</param>
    /// <param name="sagaManager">Saga state manager for querying completed sagas.</param>
    /// <param name="atProtoStorage">ATProto storage for writing final results.</param>
    /// <param name="cacheRepository">Cache repository for updating the cache.</param>
    /// <param name="deduplicator">Request deduplicator for releasing locks.</param>
    /// <param name="resultCombiner">Result combiner for assembling final results.</param>
    /// <param name="queueResolver">Queue resolver for queuing secondary provider lookups.</param>
    /// <param name="enabledProviders">Set of enabled providers for secondary lookups.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public SagaCoordinatorBackgroundService(
        IConnectionMultiplexer redis,
        ISagaStateManager sagaManager,
        IATProtoStorageService atProtoStorage,
        IMediaLinkCacheRepository cacheRepository,
        IRequestDeduplicator deduplicator,
        SagaResultCombiner resultCombiner,
        IProviderQueueResolver<QueuedLookupRequest> queueResolver,
        HashSet<SupportedProviders> enabledProviders,
        ILogger<SagaCoordinatorBackgroundService> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _sagaManager = sagaManager ?? throw new ArgumentNullException( nameof( sagaManager ) );
        _atProtoStorage = atProtoStorage ?? throw new ArgumentNullException( nameof( atProtoStorage ) );
        _cacheRepository = cacheRepository ?? throw new ArgumentNullException( nameof( cacheRepository ) );
        _deduplicator = deduplicator ?? throw new ArgumentNullException( nameof( deduplicator ) );
        _resultCombiner = resultCombiner ?? throw new ArgumentNullException( nameof( resultCombiner ) );
        _queueResolver = queueResolver ?? throw new ArgumentNullException( nameof( queueResolver ) );
        _enabledProviders = enabledProviders ?? throw new ArgumentNullException( nameof( enabledProviders ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        _logger.LogInformation(
            "Saga coordinator starting with polling interval {PollingInterval}s and minimum saga age {MinSagaAge}s",
            s_pollingInterval.TotalSeconds,
            s_minimumSagaAge.TotalSeconds
        );

        // Subscribe to saga completion events
        ISubscriber subscriber = _redis.GetSubscriber( );

        await subscriber.SubscribeAsync(
            RedisChannel.Literal( SagaCompletedChannel ),
            ( channel, message ) => {
                // Use channel to avoid analyzer warning
                if (message.HasValue && !channel.IsNullOrEmpty) {
                    _logger.LogInformation(
                        "Received saga completion event on channel {Channel}: {Message}",
                        channel,
                        message
                    );
                    // Fire-and-forget with exception handling since SubscribeAsync expects a void delegate
                    _ = Task.Run( async ( ) => {
                        try {
                            await ProcessSagaCompletionAsync( message.ToString( ), stoppingToken );
                        } catch (Exception ex) {
                            _logger.LogError( ex, "Error processing saga completion for message {Message}", message );
                        }
                    }, stoppingToken ).ConfigureAwait( false );
                }
            }
        );

        _logger.LogInformation(
            "Subscribed to Redis channel {Channel} for saga completion events",
            SagaCompletedChannel
        );

        // Subscribe to lookup complete events for partial results
        await subscriber.SubscribeAsync(
            RedisChannel.Pattern( LookupCompleteChannelPattern ),
            async ( channel, _ ) => {
                // Extract lookup key from channel (format: complete:{lookupKey})
                string channelStr = channel.ToString( );
                _logger.LogDebug( "Received lookup completion event on channel {Channel}", channelStr );
                if (!channelStr.StartsWith( "complete:", StringComparison.Ordinal )) {
                    _logger.LogDebug( "Ignoring channel {Channel} - does not match expected pattern", channelStr );
                    return;
                }

                string lookupKey = channelStr["complete:".Length..];
                string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

                try {
                    await ProcessLookupCompletionAsync( sagaId, stoppingToken );
                } catch (Exception ex) {
                    _logger.LogError( ex, "Error processing lookup completion for {LookupKey}", lookupKey );
                }
            }
        );
        _logger.LogInformation(
            "Subscribed to Redis channel pattern {Pattern} for lookup completion events",
            LookupCompleteChannelPattern
        );

        _logger.LogInformation( "Saga coordinator fully initialized, entering polling loop" );

        // Also poll periodically for any missed completions
        int pollCycleCount = 0;
        while (!stoppingToken.IsCancellationRequested) {
            pollCycleCount++;
            _logger.LogDebug( "Starting polling cycle #{CycleCount}", pollCycleCount );

            try {
                await PollForCompletedSagasAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error during saga completion polling in cycle #{CycleCount}", pollCycleCount );
            }

            _logger.LogDebug(
                "Completed polling cycle #{CycleCount}, sleeping for {Interval}s",
                pollCycleCount,
                s_pollingInterval.TotalSeconds
            );
            await Task.Delay( s_pollingInterval, stoppingToken );
        }

        // Unsubscribe on shutdown
        await subscriber.UnsubscribeAsync( RedisChannel.Literal( SagaCompletedChannel ) );
        await subscriber.UnsubscribeAsync( RedisChannel.Pattern( LookupCompleteChannelPattern ) );

        _logger.LogInformation( "Saga coordinator stopping" );
    }

    private async Task ProcessSagaCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            _logger.LogInformation( "Processing saga completion event for {SagaId}", sagaId );

            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                _logger.LogWarning( "Saga {SagaId} not found for completion processing", sagaId );
                return;
            }

            _logger.LogInformation(
                "Saga {SagaId} state: IsComplete={IsComplete}, FinalResultUri={FinalResultUri}, ProviderCount={ProviderCount}",
                sagaId,
                saga.IsComplete,
                saga.FinalResultUri ?? "(none)",
                saga.ProviderStates.Count
            );

            if (!saga.IsComplete) {
                _logger.LogInformation( "Saga {SagaId} is not yet complete, skipping finalization", sagaId );
                return;
            }

            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                _logger.LogInformation( "Saga {SagaId} already has final result at {Uri}, skipping", sagaId, saga.FinalResultUri );
                return;
            }

            await WriteFinalResultAsync( saga, ct );
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to process saga completion for {SagaId}", sagaId );
        }
    }

    private async Task ProcessLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            _logger.LogInformation( "Processing lookup completion for saga {SagaId}", sagaId );

            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                _logger.LogDebug( "Saga {SagaId} not found for lookup completion", sagaId );
                return;
            }

            _logger.LogDebug(
                "Saga {SagaId} state for lookup completion: IsComplete={IsComplete}, IsPartial={IsPartial}, PartialResultUri={PartialResultUri}",
                sagaId,
                saga.IsComplete,
                saga.IsPartial,
                saga.PartialResultUri ?? "(none)"
            );

            // If saga is complete, check cache and queue secondary lookups, then write final result
            if (saga.IsComplete) {
                // Extract the result from the saga to check for external ID
                MediaLinkResult? result = _resultCombiner.CombineResults(saga);

                if (result is not null) {
                    // Check cache for related data and queue secondary lookups for missing providers
                    await CheckCacheAndQueueSecondaryLookupsAsync( result, saga, ct );
                }

                if (string.IsNullOrEmpty( saga.FinalResultUri )) {
                    await WriteFinalResultAsync( saga, ct );
                }
                return;
            }

            // If saga is partial, handle partial result
            if (saga.IsPartial && string.IsNullOrEmpty( saga.PartialResultUri )) {
                await WritePartialResultAsync( saga, ct );
            }
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to process lookup completion for saga {SagaId}", sagaId );
        }
    }

    private async Task PollForCompletedSagasAsync( CancellationToken ct ) {
        // Check for cancellation
        ct.ThrowIfCancellationRequested( );

        _logger.LogDebug(
            "Polling for completed but unfinalized sagas (minimum age: {MinAge}s, batch limit: {Limit})",
            s_minimumSagaAge.TotalSeconds,
            PollingBatchLimit
        );

        try {
            // Query for completed but unfinalized sagas using the pending index
            IReadOnlyList<LookupSagaState> unfinalizedSagas = await _sagaManager.GetCompletedButUnfinalizedAsync(
                s_minimumSagaAge,
                PollingBatchLimit,
                ct
            );

            if (unfinalizedSagas.Count == 0) {
                _logger.LogDebug( "No unfinalized sagas found during polling" );
                return;
            }

            _logger.LogInformation(
                "Polling found {Count} completed but unfinalized sagas to process",
                unfinalizedSagas.Count
            );

            // Process each saga - these were missed by Pub/Sub
            foreach (LookupSagaState saga in unfinalizedSagas) {
                ct.ThrowIfCancellationRequested( );

                try {
                    // Check if saga is partial and needs partial result written first
                    if (saga.IsPartial && string.IsNullOrEmpty( saga.PartialResultUri )) {
                        _logger.LogDebug(
                            "Writing partial result for saga {SagaId} discovered during polling",
                            saga.SagaId
                        );
                        await WritePartialResultAsync( saga, ct );
                    }

                    // Write final result
                    _logger.LogDebug(
                        "Finalizing saga {SagaId} discovered during polling",
                        saga.SagaId
                    );
                    await WriteFinalResultAsync( saga, ct );
                } catch (Exception ex) {
                    _logger.LogError(
                        ex,
                        "Failed to process saga {SagaId} during polling, will retry next cycle",
                        saga.SagaId
                    );
                    // Continue with next saga - don't fail the entire polling cycle
                }
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError( ex, "Error querying for unfinalized sagas during polling" );
        }
    }

    private async Task WriteFinalResultAsync( LookupSagaState saga, CancellationToken ct ) {
        _logger.LogInformation(
            "Assembling final result for saga {SagaId} with {ProviderCount} provider results",
            saga.SagaId,
            saga.ProviderStates.Count
        );

        // Combine results from all successful providers
        MediaLinkResult? finalResult = _resultCombiner.CombineResults( saga );

        if (finalResult is null) {
            _logger.LogWarning(
                "Saga {SagaId} completed with no successful results, cleaning up",
                saga.SagaId
            );

            // Release the deduplication lock with null to indicate failure
            await _deduplicator.ReleaseAsync( saga.LookupKey, null, ct );

            // Delete the saga
            _ = await _sagaManager.DeleteAsync( saga.SagaId, ct );
            return;
        }

        try {
            // Write to ATProto
            string recordUri = await _atProtoStorage.StoreMediaLinkResultAsync( finalResult );

            _logger.LogDebug(
                "Wrote final result for saga {SagaId} to ATProto: {RecordUri}",
                saga.SagaId,
                recordUri
            );

            // Update saga with final result URI
            await _sagaManager.SetFinalResultUriAsync( saga.SagaId, recordUri, ct );

            // Update Redis cache
            _ = await _cacheRepository.CacheResultAsync( finalResult );

            _logger.LogDebug(
                "Cached final result for saga {SagaId}",
                saga.SagaId
            );

            // Release the deduplication lock with the result URI
            await _deduplicator.ReleaseAsync( saga.LookupKey, recordUri, ct );

            _logger.LogInformation(
                "Successfully finalized saga {SagaId} with {ProviderCount} provider results",
                saga.SagaId,
                finalResult.Results.Count
            );
        } catch (Exception ex) {
            _logger.LogError(
                ex,
                "Failed to write final result for saga {SagaId}",
                saga.SagaId
            );

            // Release the lock anyway to unblock waiters
            await _deduplicator.ReleaseAsync( saga.LookupKey, null, ct );
        }
    }

    private async Task WritePartialResultAsync( LookupSagaState saga, CancellationToken ct ) {
        _logger.LogInformation(
            "Assembling partial result for saga {SagaId} with {CompletedCount}/{TotalCount} provider results",
            saga.SagaId,
            saga.ProviderStates.Count( kv => kv.Value.IsComplete ),
            saga.ProviderStates.Count
        );

        // Combine results from completed providers (may be partial)
        MediaLinkResult? partialResult = _resultCombiner.CombineResults( saga, allowIncomplete: true );

        if (partialResult is null) {
            _logger.LogDebug(
                "Saga {SagaId} has no successful provider results yet, waiting",
                saga.SagaId
            );
            return;
        }

        // Mark the result as partial
        partialResult.IsPartial = true;
        partialResult.RateLimitedProviders = saga.RateLimitInfo?.Select( r => r.Provider ).ToList( );

        try {
            // Write partial result to ATProto
            string recordUri = await _atProtoStorage.StoreMediaLinkResultAsync( partialResult );

            _logger.LogDebug(
                "Wrote partial result for saga {SagaId} to ATProto: {RecordUri}",
                saga.SagaId,
                recordUri
            );

            // Update saga with partial result URI
            await _sagaManager.SetPartialResultUriAsync( saga.SagaId, recordUri, ct );

            // Update Redis cache with partial result (can be refreshed later)
            _ = await _cacheRepository.CacheResultAsync( partialResult );

            // Release the deduplication lock with the partial result URI
            // This allows waiting clients to receive partial results immediately
            await _deduplicator.ReleaseAsync( saga.LookupKey, recordUri, ct );

            _logger.LogInformation(
                "Successfully wrote partial result for saga {SagaId} with {ProviderCount} provider results",
                saga.SagaId,
                partialResult.Results.Count
            );
        } catch (Exception ex) {
            _logger.LogError(
                ex,
                "Failed to write partial result for saga {SagaId}",
                saga.SagaId
            );
        }
    }

    /// <summary>
    /// Publishes a saga completion event to Redis Pub/Sub.
    /// </summary>
    /// <remarks>
    /// Call this method from provider workers when a saga becomes complete.
    /// </remarks>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="sagaId">The ID of the completed saga.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    public static async Task PublishSagaCompletedAsync( IConnectionMultiplexer redis, string sagaId ) {
        ISubscriber subscriber = redis.GetSubscriber( );
        _ = await subscriber.PublishAsync( RedisChannel.Literal( SagaCompletedChannel ), sagaId );
    }

    /// <summary>
    /// Checks the cache for related data using ISRC/UPC and queues secondary lookups for missing providers.
    /// </summary>
    /// <remarks>
    /// After an initial lookup completes successfully, this method:
    /// <list type="bullet">
    ///   <item>Extracts the ISRC (tracks) or UPC (albums) from the result</item>
    ///   <item>Checks the cache for existing data from other providers</item>
    ///   <item>For providers not in cache, queues new lookup requests using the external ID</item>
    /// </list>
    /// Each secondary provider lookup creates its own separate saga.
    /// </remarks>
    private async Task CheckCacheAndQueueSecondaryLookupsAsync(
        MediaLinkResult result,
        LookupSagaState originalSaga,
        CancellationToken ct
    ) {
        // Get the first result to extract external ID
        MusicLookupResult? firstResult = result.Results.Values.FirstOrDefault();
        if (firstResult is null || string.IsNullOrWhiteSpace( firstResult.ExternalId )) {
            _logger.LogDebug(
                "No external ID in result for saga {SagaId}, skipping secondary lookups",
                originalSaga.SagaId
            );
            return;
        }

        string externalId = firstResult.ExternalId;
        bool isAlbum = firstResult.IsAlbum ?? false;
        SupportedProviders initialProvider = result.Results.Keys.First();

        // Determine which providers still need lookups (not the initial provider)
        IEnumerable<SupportedProviders> otherProviders = _enabledProviders
            .Where(p => p != initialProvider && !result.Results.ContainsKey(p));

        if (!otherProviders.Any( )) {
            _logger.LogDebug(
                "No other enabled providers for saga {SagaId}, skipping secondary lookups",
                originalSaga.SagaId
            );
            return;
        }

        // Check cache for existing data using ISRC/UPC
        LookupRequestType lookupType = isAlbum ? LookupRequestType.UpcLookup : LookupRequestType.IsrcLookup;
        (MediaLinkResult cachedResult, string recordUri, bool isStale)? cached = isAlbum
            ? await _cacheRepository.TryGetCachedResultByUPCAsync(externalId)
            : await _cacheRepository.TryGetCachedResultByISRCAsync(externalId);

        // Determine which providers we already have data for
        HashSet<SupportedProviders> providersWithData = [initialProvider];
        if (cached.HasValue && !cached.Value.isStale) {
            foreach (SupportedProviders provider in cached.Value.cachedResult.Results.Keys) {
                _ = providersWithData.Add( provider );
            }

            _logger.LogDebug(
                "Cache hit for {ExternalId}, found data for providers: {Providers}",
                externalId,
                string.Join( ", ", providersWithData )
            );
        }

        // Queue lookups for providers we don't have data for
        foreach (SupportedProviders provider in otherProviders) {
            if (providersWithData.Contains( provider )) {
                _logger.LogDebug(
                    "Skipping {Provider} lookup for {ExternalId} - data already in cache",
                    provider,
                    externalId
                );
                continue;
            }

            // Generate a unique lookup key for this secondary lookup
            string lookupKey = $"{lookupType}:{externalId}:{provider}";
            string sagaId = ISagaStateManager.GenerateSagaId(lookupKey);

            // Check if there's already a saga for this lookup
            LookupSagaState? existingSaga = await _sagaManager.GetAsync(sagaId, ct);
            if (existingSaga is not null) {
                _logger.LogDebug(
                    "Saga {SagaId} already exists for {Provider} lookup of {ExternalId}, skipping",
                    sagaId,
                    provider,
                    externalId
                );
                continue;
            }

            try {
                // Create a new saga for this secondary lookup
                // Use lookupKey as the lookupValue to maintain consistency with saga identification
                _ = await _sagaManager.GetOrCreateAsync( sagaId, lookupKey, lookupType, lookupKey );
                await _sagaManager.InitializeProviderStatesAsync( sagaId, [provider] );

                // Create and queue the lookup request
                QueuedLookupRequest secondaryRequest = new()
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Provider = provider,
                    LookupType = lookupType,
                    LookupValue = externalId,
                    SagaId = sagaId,
                    IsAlbum = isAlbum,
                    Title = firstResult.Title,
                    Artist = firstResult.Artist
                };

                IRequestQueue<QueuedLookupRequest> queue = _queueResolver.GetQueue(provider);
                await queue.EnqueueAsync( secondaryRequest, QueuePriority.Background, ct );

                _logger.LogInformation(
                    "Queued secondary {LookupType} lookup for {Provider} with external ID {ExternalId} (saga {SagaId})",
                    lookupType,
                    provider,
                    externalId,
                    sagaId
                );
            } catch (Exception ex) {
                _logger.LogError(
                    ex,
                    "Failed to queue secondary lookup for {Provider} with external ID {ExternalId}",
                    provider,
                    externalId
                );
                // Continue with other providers - don't fail the entire operation
            }
        }
    }
}
