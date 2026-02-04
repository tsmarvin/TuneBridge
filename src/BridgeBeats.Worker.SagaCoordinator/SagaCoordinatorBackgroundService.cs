using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services.Queue;
using BridgeBeats.Worker.SagaCoordinator.Logging;
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
/// <remarks>
/// Initializes a new instance of the <see cref="SagaCoordinatorBackgroundService"/> class.
/// </remarks>
/// <param name="redis">Redis connection for Pub/Sub subscriptions.</param>
/// <param name="sagaManager">Saga state manager for querying completed sagas.</param>
/// <param name="atProtoStorage">ATProto storage for writing final results.</param>
/// <param name="cacheRepository">Cache repository for updating the cache.</param>
/// <param name="deduplicator">Request deduplicator for releasing locks.</param>
/// <param name="resultCombiner">Result combiner for assembling final results.</param>
/// <param name="queueResolver">Queue resolver for queuing secondary provider lookups.</param>
/// <param name="enabledProviders">Set of enabled providers for secondary lookups.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed partial class SagaCoordinatorBackgroundService(
    IConnectionMultiplexer redis,
    ISagaStateManager sagaManager,
    IATProtoStorageService atProtoStorage,
    IMediaLinkCacheRepository cacheRepository,
    IRequestDeduplicator deduplicator,
    SagaResultCombiner resultCombiner,
    IProviderQueueResolver<QueuedLookupRequest> queueResolver,
    HashSet<SupportedProviders> enabledProviders,
    ILogger<SagaCoordinatorBackgroundService> logger
    ) : BackgroundService {
    private readonly IConnectionMultiplexer _redis = redis
                                                   ?? throw new ArgumentNullException( nameof( redis ) );
    private readonly ISagaStateManager _sagaManager = sagaManager
                                                    ?? throw new ArgumentNullException( nameof( sagaManager ) );
    private readonly IATProtoStorageService _atProtoStorage = atProtoStorage
                                                            ?? throw new ArgumentNullException( nameof( atProtoStorage ) );
    private readonly IMediaLinkCacheRepository _cacheRepository = cacheRepository
                                                                ?? throw new ArgumentNullException( nameof( cacheRepository ) );
    private readonly IRequestDeduplicator _deduplicator = deduplicator
                                                        ?? throw new ArgumentNullException( nameof( deduplicator ) );
    private readonly SagaResultCombiner _resultCombiner = resultCombiner
                                                        ?? throw new ArgumentNullException( nameof( resultCombiner ) );
    private readonly IProviderQueueResolver<QueuedLookupRequest> _queueResolver = queueResolver
                                                                                ?? throw new ArgumentNullException( nameof( queueResolver ) );
    private readonly HashSet<SupportedProviders> _enabledProviders = enabledProviders
                                                                   ?? throw new ArgumentNullException( nameof( enabledProviders ) );
    private readonly ILogger<SagaCoordinatorBackgroundService> _logger = logger
                                                                       ?? throw new ArgumentNullException( nameof( logger ) );

    private const string SagaCompletedChannel = "saga:completed";
    private const string LookupCompleteChannelPattern = "complete:*";
    private static readonly TimeSpan s_pollingInterval = TimeSpan.FromSeconds( 30 );
    private static readonly TimeSpan s_minimumSagaAge = TimeSpan.FromSeconds( 15 );
    private const int PollingBatchLimit = 100;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogStarting(
            _logger,
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
                    if (_logger.IsEnabled( LogLevel.Information )) {
                        string channelStr = channel.ToString( );
                        string messageStr = message.ToString( );
                        LogReceivedSagaCompletionEvent(
                            _logger,
                            channelStr,
                            messageStr
                        );
                    }
                    // Fire-and-forget with exception handling since SubscribeAsync expects a void delegate
                    _ = Task.Run( async ( ) => {
                        try {
                            await ProcessSagaCompletionAsync( message.ToString( ), stoppingToken );
                        } catch (Exception ex) {
                            LogSagaCompletionError( _logger, ex, message.ToString( ) );
                        }
                    }, stoppingToken ).ConfigureAwait( false );
                }
            }
        );

        LogSubscribedToChannel( _logger, SagaCompletedChannel );

        // Subscribe to lookup complete events for partial results
        await subscriber.SubscribeAsync(
            RedisChannel.Pattern( LookupCompleteChannelPattern ),
            async ( channel, _ ) => {
                // Extract lookup key from channel (format: complete:{lookupKey})
                string channelStr = channel.ToString( );
                LogReceivedLookupCompletionEvent( _logger, channelStr );
                if (!channelStr.StartsWith( "complete:", StringComparison.Ordinal )) {
                    LogIgnoringChannel( _logger, channelStr );
                    return;
                }

                string lookupKey = channelStr["complete:".Length..];
                string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );

                try {
                    await ProcessLookupCompletionAsync( sagaId, stoppingToken );
                } catch (Exception ex) {
                    LogLookupCompletionError( _logger, ex, lookupKey );
                }
            }
        );
        LogSubscribedToPattern( _logger, LookupCompleteChannelPattern );

        LogFullyInitialized( _logger );

        // Also poll periodically for any missed completions
        int pollCycleCount = 0;
        while (!stoppingToken.IsCancellationRequested) {
            pollCycleCount++;
            LogStartingPollingCycle( _logger, pollCycleCount );

            try {
                await PollForCompletedSagasAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogPollingError( _logger, ex, pollCycleCount );
            }

            LogCompletedPollingCycle( _logger, pollCycleCount, s_pollingInterval.TotalSeconds );
            await Task.Delay( s_pollingInterval, stoppingToken );
        }

        // Unsubscribe on shutdown
        await subscriber.UnsubscribeAsync( RedisChannel.Literal( SagaCompletedChannel ) );
        await subscriber.UnsubscribeAsync( RedisChannel.Pattern( LookupCompleteChannelPattern ) );

        LogStopping( _logger );
    }

    private async Task ProcessSagaCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            LogProcessingSagaCompletion( _logger, sagaId );

            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                LogSagaNotFound( _logger, sagaId );
                return;
            }

            LogSagaState(
                _logger,
                sagaId,
                saga.IsComplete,
                saga.FinalResultUri ?? "(none)",
                saga.ProviderStates.Count
            );

            if (!saga.IsComplete) {
                LogSagaNotComplete( _logger, sagaId );
                return;
            }

            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                LogSagaAlreadyFinalized( _logger, sagaId, saga.FinalResultUri );
                return;
            }

            // Extract the result to check for external ID and queue secondary lookups
            MediaLinkResult? result = _resultCombiner.CombineResults( saga );
            if (result is not null) {
                bool queuedSecondaryLookups = await CheckCacheAndQueueSecondaryLookupsAsync( result, saga, ct );
                if (queuedSecondaryLookups) {
                    // Secondary lookups were queued - don't finalize yet, wait for them to complete
                    LogWaitingForSecondaryLookups( _logger, sagaId );
                    return;
                }
            }

            await WriteFinalResultAsync( saga, ct );
        } catch (Exception ex) {
            LogFailedToProcessSaga( _logger, ex, sagaId );
        }
    }

    private async Task ProcessLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            LogProcessingLookupCompletion( _logger, sagaId );

            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                LogSagaNotFoundForLookup( _logger, sagaId );
                return;
            }

            LogSagaStateForLookup(
                _logger,
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
                    bool queuedSecondaryLookups = await CheckCacheAndQueueSecondaryLookupsAsync( result, saga, ct );
                    if (queuedSecondaryLookups) {
                        // Secondary lookups were queued - don't finalize yet, wait for them to complete
                        LogWaitingForSecondaryLookups( _logger, sagaId );
                        return;
                    }
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
            LogFailedLookupCompletion( _logger, ex, sagaId );
        }
    }

    private async Task PollForCompletedSagasAsync( CancellationToken ct ) {
        // Check for cancellation
        ct.ThrowIfCancellationRequested( );

        LogPollingForSagas( _logger, s_minimumSagaAge.TotalSeconds, PollingBatchLimit );

        try {
            // Query for completed but unfinalized sagas using the pending index
            IReadOnlyList<LookupSagaState> unfinalizedSagas = await _sagaManager.GetCompletedButUnfinalizedAsync(
                s_minimumSagaAge,
                PollingBatchLimit,
                ct
            );

            if (unfinalizedSagas.Count == 0) {
                LogNoUnfinalizedSagas( _logger );
                return;
            }

            LogFoundUnfinalizedSagas( _logger, unfinalizedSagas.Count );

            // Process each saga - these were missed by Pub/Sub
            foreach (LookupSagaState saga in unfinalizedSagas) {
                ct.ThrowIfCancellationRequested( );

                try {
                    // Check if saga is partial and needs partial result written first
                    if (saga.IsPartial && string.IsNullOrEmpty( saga.PartialResultUri )) {
                        LogWritingPartialDuringPolling( _logger, saga.SagaId );
                        await WritePartialResultAsync( saga, ct );
                    }

                    // Write final result
                    LogFinalizingDuringPolling( _logger, saga.SagaId );
                    await WriteFinalResultAsync( saga, ct );
                } catch (Exception ex) {
                    LogFailedDuringPolling( _logger, ex, saga.SagaId );
                    // Continue with next saga - don't fail the entire polling cycle
                }
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            LogQueryError( _logger, ex );
        }
    }

    private async Task WriteFinalResultAsync( LookupSagaState saga, CancellationToken ct ) {
        LogAssemblingFinalResult( _logger, saga.SagaId, saga.ProviderStates.Count );

        // Combine results from all successful providers
        MediaLinkResult? finalResult = _resultCombiner.CombineResults( saga );

        if (finalResult is null) {
            LogNoSuccessfulResults( _logger, saga.SagaId );

            // Release the deduplication lock with null to indicate failure
            await _deduplicator.ReleaseAsync( saga.LookupKey, null, ct );

            // Delete the saga
            _ = await _sagaManager.DeleteAsync( saga.SagaId, ct );
            return;
        }

        try {
            // Write to ATProto
            string recordUri = await _atProtoStorage.StoreMediaLinkResultAsync( finalResult );

            LogWroteFinalToAtProto( _logger, saga.SagaId, recordUri );

            // Update saga with final result URI
            await _sagaManager.SetFinalResultUriAsync( saga.SagaId, recordUri, ct );

            // Update Redis cache
            _ = await _cacheRepository.CacheResultAsync( finalResult );

            LogCachedFinalResult( _logger, saga.SagaId );

            // Release the deduplication lock with the result URI
            await _deduplicator.ReleaseAsync( saga.LookupKey, recordUri, ct );

            LogSuccessfullyFinalized( _logger, saga.SagaId, finalResult.Results.Count );
        } catch (Exception ex) {
            LogFailedToWriteFinal( _logger, ex, saga.SagaId );

            // Release the lock anyway to unblock waiters
            await _deduplicator.ReleaseAsync( saga.LookupKey, null, ct );
        }
    }

    private async Task WritePartialResultAsync( LookupSagaState saga, CancellationToken ct ) {
        if (_logger.IsEnabled( LogLevel.Debug )) {
            int completedCount = saga.ProviderStates.Count( kv => kv.Value.IsComplete );
            int totalCount = saga.ProviderStates.Count;
            LogAssemblingPartialResult(
                _logger,
                saga.SagaId,
                completedCount,
                totalCount
            );
        }

        // Combine results from completed providers (may be partial)
        MediaLinkResult? partialResult = _resultCombiner.CombineResults( saga, allowIncomplete: true );

        if (partialResult is null) {
            LogNoSuccessfulPartialResults( _logger, saga.SagaId );
            return;
        }

        // Mark the result as partial
        partialResult.IsPartial = true;
        partialResult.RateLimitedProviders = saga.RateLimitInfo?.Select( r => r.Provider ).ToList( );

        try {
            // Write partial result to ATProto
            string recordUri = await _atProtoStorage.StoreMediaLinkResultAsync( partialResult );

            LogWrotePartialToAtProto( _logger, saga.SagaId, recordUri );

            // Update saga with partial result URI
            await _sagaManager.SetPartialResultUriAsync( saga.SagaId, recordUri, ct );

            // Update Redis cache with partial result (can be refreshed later)
            _ = await _cacheRepository.CacheResultAsync( partialResult );

            // Release the deduplication lock with the partial result URI
            // This allows waiting clients to receive partial results immediately
            await _deduplicator.ReleaseAsync( saga.LookupKey, recordUri, ct );

            LogSuccessfullyWrotePartial( _logger, saga.SagaId, partialResult.Results.Count );
        } catch (Exception ex) {
            LogFailedToWritePartial( _logger, ex, saga.SagaId );
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
    ///   <item>For providers not in cache, adds them to the original saga and queues lookup requests</item>
    /// </list>
    /// Secondary lookups are added to the original saga, not separate sagas.
    /// </remarks>
    /// <returns>True if secondary lookups were queued (caller should wait), false if ready to finalize.</returns>
    private async Task<bool> CheckCacheAndQueueSecondaryLookupsAsync(
        MediaLinkResult result,
        LookupSagaState originalSaga,
        CancellationToken ct
    ) {
        // Don't trigger secondary lookups from secondary lookups (ISRC/UPC lookups)
        // Only provider-specific lookups (SpotifyLookup, AppleMusicLookup, TidalLookup) should trigger secondary lookups
        if (originalSaga.LookupType is LookupRequestType.IsrcLookup or LookupRequestType.UpcLookup) {
            return false;
        }

        // Get the first result to extract external ID
        MusicLookupResult? firstResult = result.Results.Values.FirstOrDefault();
        if (firstResult is null || string.IsNullOrWhiteSpace( firstResult.ExternalId )) {
            LogNoExternalId( _logger, originalSaga.SagaId );
            return false;
        }

        string externalId = firstResult.ExternalId;
        bool isAlbum = firstResult.IsAlbum ?? false;
        SupportedProviders initialProvider = result.Results.Keys.First();

        // Determine which providers still need lookups (not the initial provider)
        List<SupportedProviders> otherProviders = _enabledProviders
            .Where(p => p != initialProvider && !result.Results.ContainsKey(p))
            .ToList();

        if (otherProviders.Count == 0) {
            LogNoOtherProviders( _logger, originalSaga.SagaId );
            return false;
        }

        // Check cache for existing data using ISRC/UPC
        LookupRequestType lookupType = isAlbum ? LookupRequestType.UpcLookup : LookupRequestType.IsrcLookup;
        (MediaLinkResult cachedResult, string recordUri, bool isStale)? cached = isAlbum
            ? await _cacheRepository.TryGetCachedResultByUPCAsync(externalId)
            : await _cacheRepository.TryGetCachedResultByISRCAsync(externalId);

        // Determine which providers we already have data for (from result, cache, or already in saga)
        HashSet<SupportedProviders> providersWithData = [initialProvider];

        // Add providers already initialized in the saga
        foreach (SupportedProviders provider in originalSaga.ProviderStates.Keys) {
            _ = providersWithData.Add( provider );
        }

        // Add providers with cached data
        if (cached.HasValue && !cached.Value.isStale) {
            foreach (SupportedProviders provider in cached.Value.cachedResult.Results.Keys) {
                _ = providersWithData.Add( provider );
            }

            if (_logger.IsEnabled( LogLevel.Debug )) {
                string providersStr = string.Join( ", ", providersWithData );
                LogCacheHitForExternalId( _logger, externalId, providersStr );
            }
        }

        // Find providers that need lookups (not already in saga or cache)
        List<SupportedProviders> providersToQueue = otherProviders
            .Where(p => !providersWithData.Contains(p))
            .ToList();

        if (providersToQueue.Count == 0) {
            LogAllProvidersInCache( _logger, originalSaga.SagaId, externalId );
            return false;
        }

        // Add the new providers to the ORIGINAL saga (don't create separate sagas)
        await _sagaManager.InitializeProviderStatesAsync( originalSaga.SagaId, providersToQueue );
        LogAddedProvidersToSaga( _logger, originalSaga.SagaId, string.Join( ", ", providersToQueue ) );

        // Queue lookups for each provider using the ORIGINAL saga ID
        int queuedCount = 0;
        foreach (SupportedProviders provider in providersToQueue) {
            try {
                // Create and queue the lookup request using the ORIGINAL saga ID
                QueuedLookupRequest secondaryRequest = new()
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Provider = provider,
                    LookupType = lookupType,
                    LookupValue = externalId,
                    SagaId = originalSaga.SagaId,  // Use original saga ID!
                    IsAlbum = isAlbum,
                    Title = firstResult.Title,
                    Artist = firstResult.Artist
                };

                IRequestQueue<QueuedLookupRequest> queue = _queueResolver.GetQueue(provider);
                await queue.EnqueueAsync( secondaryRequest, QueuePriority.Background, ct );

                string lookupTypeStr = lookupType.ToString( );
                string providerStr = provider.ToString( );
                LogQueuedSecondaryLookup( _logger, lookupTypeStr, providerStr, externalId, originalSaga.SagaId );
                queuedCount++;
            } catch (Exception ex) {
                string providerStr = provider.ToString( );
                LogFailedToQueueSecondary( _logger, ex, providerStr, externalId );
                // Continue with other providers - don't fail the entire operation
            }
        }

        // Return true if we queued any secondary lookups (caller should wait for completion)
        return queuedCount > 0;
    }

    #region LoggerMessage Methods

    /// <summary>Logs that the saga coordinator is starting.</summary>
    [LoggerMessage(
        EventId = LogEventIds.Starting,
        Level = LogLevel.Information,
        Message = "Saga coordinator starting with polling interval {PollingInterval}s and minimum saga age {MinSagaAge}s" )]
    private static partial void LogStarting( ILogger logger, double pollingInterval, double minSagaAge );

    /// <summary>Logs that a saga completion event was received.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ReceivedSagaCompletionEvent,
        Level = LogLevel.Information,
        Message = "Received saga completion event on channel {Channel}: {Message}" )]
    private static partial void LogReceivedSagaCompletionEvent( ILogger logger, string channel, string message );

    /// <summary>Logs an error during saga completion processing.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaCompletionError,
        Level = LogLevel.Error,
        Message = "Error processing saga completion for message {Message}" )]
    private static partial void LogSagaCompletionError( ILogger logger, Exception ex, string message );

    /// <summary>Logs that the service subscribed to a channel.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SubscribedToChannel,
        Level = LogLevel.Information,
        Message = "Subscribed to Redis channel {Channel} for saga completion events" )]
    private static partial void LogSubscribedToChannel( ILogger logger, string channel );

    /// <summary>Logs that a lookup completion event was received.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ReceivedLookupCompletionEvent,
        Level = LogLevel.Debug,
        Message = "Received lookup completion event on channel {Channel}" )]
    private static partial void LogReceivedLookupCompletionEvent( ILogger logger, string channel );

    /// <summary>Logs that a channel is being ignored.</summary>
    [LoggerMessage(
        EventId = LogEventIds.IgnoringChannel,
        Level = LogLevel.Debug,
        Message = "Ignoring channel {Channel} - does not match expected pattern" )]
    private static partial void LogIgnoringChannel( ILogger logger, string channel );

    /// <summary>Logs an error during lookup completion processing.</summary>
    [LoggerMessage(
        EventId = LogEventIds.LookupCompletionError,
        Level = LogLevel.Error,
        Message = "Error processing lookup completion for {LookupKey}" )]
    private static partial void LogLookupCompletionError( ILogger logger, Exception ex, string lookupKey );

    /// <summary>Logs that the service subscribed to a pattern.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SubscribedToPattern,
        Level = LogLevel.Information,
        Message = "Subscribed to Redis channel pattern {Pattern} for lookup completion events" )]
    private static partial void LogSubscribedToPattern( ILogger logger, string pattern );

    /// <summary>Logs that the saga coordinator is fully initialized.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FullyInitialized,
        Level = LogLevel.Information,
        Message = "Saga coordinator fully initialized, entering polling loop" )]
    private static partial void LogFullyInitialized( ILogger logger );

    /// <summary>Logs that a polling cycle is starting.</summary>
    [LoggerMessage(
        EventId = LogEventIds.StartingPollingCycle,
        Level = LogLevel.Debug,
        Message = "Starting polling cycle #{CycleCount}" )]
    private static partial void LogStartingPollingCycle( ILogger logger, int cycleCount );

    /// <summary>Logs an error during polling.</summary>
    [LoggerMessage(
        EventId = LogEventIds.PollingError,
        Level = LogLevel.Error,
        Message = "Error during saga completion polling in cycle #{CycleCount}" )]
    private static partial void LogPollingError( ILogger logger, Exception ex, int cycleCount );

    /// <summary>Logs that a polling cycle completed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.CompletedPollingCycle,
        Level = LogLevel.Debug,
        Message = "Completed polling cycle #{CycleCount}, sleeping for {Interval}s" )]
    private static partial void LogCompletedPollingCycle( ILogger logger, int cycleCount, double interval );

    /// <summary>Logs that the saga coordinator is stopping.</summary>
    [LoggerMessage(
        EventId = LogEventIds.Stopping,
        Level = LogLevel.Information,
        Message = "Saga coordinator stopping" )]
    private static partial void LogStopping( ILogger logger );

    /// <summary>Logs that saga completion is being processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingSagaCompletion,
        Level = LogLevel.Information,
        Message = "Processing saga completion event for {SagaId}" )]
    private static partial void LogProcessingSagaCompletion( ILogger logger, string sagaId );

    /// <summary>Logs that a saga was not found.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaNotFound,
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} not found for completion processing" )]
    private static partial void LogSagaNotFound( ILogger logger, string sagaId );

    /// <summary>Logs saga state information.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaState,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} state: IsComplete={IsComplete}, FinalResultUri={FinalResultUri}, ProviderCount={ProviderCount}" )]
    private static partial void LogSagaState( ILogger logger, string sagaId, bool isComplete, string finalResultUri, int providerCount );

    /// <summary>Logs that a saga is not yet complete.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaNotComplete,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} is not yet complete, skipping finalization" )]
    private static partial void LogSagaNotComplete( ILogger logger, string sagaId );

    /// <summary>Logs that a saga is already finalized.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaAlreadyFinalized,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} already has final result at {Uri}, skipping" )]
    private static partial void LogSagaAlreadyFinalized( ILogger logger, string sagaId, string uri );

    /// <summary>Logs that saga processing failed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FailedToProcessSaga,
        Level = LogLevel.Error,
        Message = "Failed to process saga completion for {SagaId}" )]
    private static partial void LogFailedToProcessSaga( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs that lookup completion is being processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingLookupCompletion,
        Level = LogLevel.Information,
        Message = "Processing lookup completion for saga {SagaId}" )]
    private static partial void LogProcessingLookupCompletion( ILogger logger, string sagaId );

    /// <summary>Logs that a saga was not found for lookup completion.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaNotFoundForLookup,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} not found for lookup completion" )]
    private static partial void LogSagaNotFoundForLookup( ILogger logger, string sagaId );

    /// <summary>Logs saga state for lookup completion.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaStateForLookup,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} state for lookup completion: IsComplete={IsComplete}, IsPartial={IsPartial}, PartialResultUri={PartialResultUri}" )]
    private static partial void LogSagaStateForLookup( ILogger logger, string sagaId, bool isComplete, bool isPartial, string partialResultUri );

    /// <summary>Logs that lookup completion failed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FailedLookupCompletion,
        Level = LogLevel.Error,
        Message = "Failed to process lookup completion for saga {SagaId}" )]
    private static partial void LogFailedLookupCompletion( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs that the service is polling for sagas.</summary>
    [LoggerMessage(
        EventId = LogEventIds.PollingForSagas,
        Level = LogLevel.Debug,
        Message = "Polling for completed but unfinalized sagas (minimum age: {MinAge}s, batch limit: {Limit})" )]
    private static partial void LogPollingForSagas( ILogger logger, double minAge, int limit );

    /// <summary>Logs that no unfinalized sagas were found.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoUnfinalizedSagas,
        Level = LogLevel.Debug,
        Message = "No unfinalized sagas found during polling" )]
    private static partial void LogNoUnfinalizedSagas( ILogger logger );

    /// <summary>Logs that unfinalized sagas were found.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FoundUnfinalizedSagas,
        Level = LogLevel.Information,
        Message = "Polling found {Count} completed but unfinalized sagas to process" )]
    private static partial void LogFoundUnfinalizedSagas( ILogger logger, int count );

    /// <summary>Logs that a partial result is being written during polling.</summary>
    [LoggerMessage(
        EventId = LogEventIds.WritingPartialDuringPolling,
        Level = LogLevel.Debug,
        Message = "Writing partial result for saga {SagaId} discovered during polling" )]
    private static partial void LogWritingPartialDuringPolling( ILogger logger, string sagaId );

    /// <summary>Logs that a saga is being finalized during polling.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FinalizingDuringPolling,
        Level = LogLevel.Debug,
        Message = "Finalizing saga {SagaId} discovered during polling" )]
    private static partial void LogFinalizingDuringPolling( ILogger logger, string sagaId );

    /// <summary>Logs that saga processing failed during polling.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FailedDuringPolling,
        Level = LogLevel.Error,
        Message = "Failed to process saga {SagaId} during polling, will retry next cycle" )]
    private static partial void LogFailedDuringPolling( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs an error querying for unfinalized sagas.</summary>
    [LoggerMessage(
        EventId = LogEventIds.QueryError,
        Level = LogLevel.Error,
        Message = "Error querying for unfinalized sagas during polling" )]
    private static partial void LogQueryError( ILogger logger, Exception ex );

    /// <summary>Logs that final result is being assembled.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AssemblingFinalResult,
        Level = LogLevel.Information,
        Message = "Assembling final result for saga {SagaId} with {ProviderCount} provider results" )]
    private static partial void LogAssemblingFinalResult( ILogger logger, string sagaId, int providerCount );

    /// <summary>Logs that saga completed with no successful results.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoSuccessfulResults,
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} completed with no successful results, cleaning up" )]
    private static partial void LogNoSuccessfulResults( ILogger logger, string sagaId );

    /// <summary>Logs that final result was written to ATProto.</summary>
    [LoggerMessage(
        EventId = LogEventIds.WroteFinalToAtProto,
        Level = LogLevel.Debug,
        Message = "Wrote final result for saga {SagaId} to ATProto: {RecordUri}" )]
    private static partial void LogWroteFinalToAtProto( ILogger logger, string sagaId, string recordUri );

    /// <summary>Logs that final result was cached.</summary>
    [LoggerMessage(
        EventId = LogEventIds.CachedFinalResult,
        Level = LogLevel.Debug,
        Message = "Cached final result for saga {SagaId}" )]
    private static partial void LogCachedFinalResult( ILogger logger, string sagaId );

    /// <summary>Logs that saga was successfully finalized.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SuccessfullyFinalized,
        Level = LogLevel.Information,
        Message = "Successfully finalized saga {SagaId} with {ProviderCount} provider results" )]
    private static partial void LogSuccessfullyFinalized( ILogger logger, string sagaId, int providerCount );

    /// <summary>Logs that writing final result failed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FailedToWriteFinal,
        Level = LogLevel.Error,
        Message = "Failed to write final result for saga {SagaId}" )]
    private static partial void LogFailedToWriteFinal( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs that partial result is being assembled.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AssemblingPartialResult,
        Level = LogLevel.Information,
        Message = "Assembling partial result for saga {SagaId} with {CompletedCount}/{TotalCount} provider results" )]
    private static partial void LogAssemblingPartialResult( ILogger logger, string sagaId, int completedCount, int totalCount );

    /// <summary>Logs that saga has no successful partial results.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoSuccessfulPartialResults,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} has no successful provider results yet, waiting" )]
    private static partial void LogNoSuccessfulPartialResults( ILogger logger, string sagaId );

    /// <summary>Logs that partial result was written to ATProto.</summary>
    [LoggerMessage(
        EventId = LogEventIds.WrotePartialToAtProto,
        Level = LogLevel.Debug,
        Message = "Wrote partial result for saga {SagaId} to ATProto: {RecordUri}" )]
    private static partial void LogWrotePartialToAtProto( ILogger logger, string sagaId, string recordUri );

    /// <summary>Logs that partial result was successfully written.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SuccessfullyWrotePartial,
        Level = LogLevel.Information,
        Message = "Successfully wrote partial result for saga {SagaId} with {ProviderCount} provider results" )]
    private static partial void LogSuccessfullyWrotePartial( ILogger logger, string sagaId, int providerCount );

    /// <summary>Logs that writing partial result failed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FailedToWritePartial,
        Level = LogLevel.Error,
        Message = "Failed to write partial result for saga {SagaId}" )]
    private static partial void LogFailedToWritePartial( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs that no external ID is in the result.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoExternalId,
        Level = LogLevel.Information,
        Message = "No external ID in result for saga {SagaId}, skipping secondary lookups" )]
    private static partial void LogNoExternalId( ILogger logger, string sagaId );

    /// <summary>Logs that no other enabled providers exist.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoOtherProviders,
        Level = LogLevel.Information,
        Message = "No other enabled providers for saga {SagaId}, skipping secondary lookups" )]
    private static partial void LogNoOtherProviders( ILogger logger, string sagaId );

    /// <summary>Logs a cache hit for an external ID.</summary>
    [LoggerMessage(
        EventId = LogEventIds.CacheHitForExternalId,
        Level = LogLevel.Debug,
        Message = "Cache hit for {ExternalId}, found data for providers: {Providers}" )]
    private static partial void LogCacheHitForExternalId( ILogger logger, string externalId, string providers );

    /// <summary>Logs that all providers already have data in cache.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AllProvidersInCache,
        Level = LogLevel.Information,
        Message = "All providers already in cache for saga {SagaId} with external ID {ExternalId}, no secondary lookups needed" )]
    private static partial void LogAllProvidersInCache( ILogger logger, string sagaId, string externalId );

    /// <summary>Logs that providers were added to an existing saga.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AddedProvidersToSaga,
        Level = LogLevel.Information,
        Message = "Added providers [{Providers}] to existing saga {SagaId} for secondary lookups" )]
    private static partial void LogAddedProvidersToSaga( ILogger logger, string sagaId, string providers );

    /// <summary>Logs that a secondary lookup was queued.</summary>
    [LoggerMessage(
        EventId = LogEventIds.QueuedSecondaryLookup,
        Level = LogLevel.Information,
        Message = "Queued secondary {LookupType} lookup for {Provider} with external ID {ExternalId} (saga {SagaId})" )]
    private static partial void LogQueuedSecondaryLookup( ILogger logger, string lookupType, string provider, string externalId, string sagaId );

    /// <summary>Logs that queuing a secondary lookup failed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.FailedToQueueSecondary,
        Level = LogLevel.Error,
        Message = "Failed to queue secondary lookup for {Provider} with external ID {ExternalId}" )]
    private static partial void LogFailedToQueueSecondary( ILogger logger, Exception ex, string provider, string externalId );

    /// <summary>Logs that the saga coordinator is waiting for secondary lookups to complete.</summary>
    [LoggerMessage(
        EventId = LogEventIds.WaitingForSecondaryLookups,
        Level = LogLevel.Information,
        Message = "Waiting for secondary lookups to complete for saga {SagaId}, deferring finalization" )]
    private static partial void LogWaitingForSecondaryLookups( ILogger logger, string sagaId );

    #endregion
}
