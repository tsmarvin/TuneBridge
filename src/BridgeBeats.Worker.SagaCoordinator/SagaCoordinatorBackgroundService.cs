using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
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
    /// <param name="logger">Logger for diagnostic information.</param>
    public SagaCoordinatorBackgroundService(
        IConnectionMultiplexer redis,
        ISagaStateManager sagaManager,
        IATProtoStorageService atProtoStorage,
        IMediaLinkCacheRepository cacheRepository,
        IRequestDeduplicator deduplicator,
        SagaResultCombiner resultCombiner,
        ILogger<SagaCoordinatorBackgroundService> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _sagaManager = sagaManager ?? throw new ArgumentNullException( nameof( sagaManager ) );
        _atProtoStorage = atProtoStorage ?? throw new ArgumentNullException( nameof( atProtoStorage ) );
        _cacheRepository = cacheRepository ?? throw new ArgumentNullException( nameof( cacheRepository ) );
        _deduplicator = deduplicator ?? throw new ArgumentNullException( nameof( deduplicator ) );
        _resultCombiner = resultCombiner ?? throw new ArgumentNullException( nameof( resultCombiner ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        _logger.LogInformation( "Saga coordinator starting" );

        // Subscribe to saga completion events
        ISubscriber subscriber = _redis.GetSubscriber( );

        await subscriber.SubscribeAsync(
            RedisChannel.Literal( SagaCompletedChannel ),
            ( channel, message ) => {
                // Use channel to avoid analyzer warning
                if (message.HasValue && !channel.IsNullOrEmpty) {
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
                if (!channelStr.StartsWith( "complete:", StringComparison.Ordinal )) {
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

        // Also poll periodically for any missed completions
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await PollForCompletedSagasAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error during saga completion polling" );
            }

            await Task.Delay( s_pollingInterval, stoppingToken );
        }

        // Unsubscribe on shutdown
        await subscriber.UnsubscribeAsync( RedisChannel.Literal( SagaCompletedChannel ) );
        await subscriber.UnsubscribeAsync( RedisChannel.Pattern( LookupCompleteChannelPattern ) );

        _logger.LogInformation( "Saga coordinator stopping" );
    }

    private async Task ProcessSagaCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            _logger.LogDebug( "Processing saga completion event for {SagaId}", sagaId );

            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                _logger.LogWarning( "Saga {SagaId} not found for completion processing", sagaId );
                return;
            }

            if (!saga.IsComplete) {
                _logger.LogDebug( "Saga {SagaId} is not yet complete, skipping", sagaId );
                return;
            }

            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                _logger.LogDebug( "Saga {SagaId} already has final result, skipping", sagaId );
                return;
            }

            await WriteFinalResultAsync( saga, ct );
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to process saga completion for {SagaId}", sagaId );
        }
    }

    private async Task ProcessLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            _logger.LogDebug( "Processing lookup completion for saga {SagaId}", sagaId );

            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                _logger.LogDebug( "Saga {SagaId} not found for lookup completion", sagaId );
                return;
            }

            // If saga is complete, handle final result
            if (saga.IsComplete) {
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

        _logger.LogTrace( "Polling for completed sagas" );

        try {
            // Query for completed but unfinalized sagas using the pending index
            IReadOnlyList<LookupSagaState> unfinalizedSagas = await _sagaManager.GetCompletedButUnfinalizedAsync(
                s_minimumSagaAge,
                PollingBatchLimit,
                ct
            );

            if (unfinalizedSagas.Count == 0) {
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
}
