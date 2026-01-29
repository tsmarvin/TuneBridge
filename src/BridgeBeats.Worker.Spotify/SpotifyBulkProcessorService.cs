using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Spotify;
using StackExchange.Redis;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Background service that processes bulk ID lookups for Spotify when thresholds are met.
/// </summary>
/// <remarks>
/// <para>
/// This service monitors queue depth for track and album ID lookups across all priority queues.
/// When the threshold is met (50 tracks or 20 albums), it batch-dequeues requests and calls
/// the Spotify bulk lookup APIs for maximum efficiency.
/// </para>
/// <para>
/// The service:
/// <list type="bullet">
///   <item>Periodically checks ID-lookup queue depth across all priorities</item>
///   <item>When threshold met: batch-dequeues and calls bulk API</item>
///   <item>Processes results: null → mark not-found; valid → update saga; exception → requeue</item>
///   <item>Handles rate limiting with separate endpoint keys for bulk operations</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SpotifyBulkProcessorService : BackgroundService {

    private readonly IConnectionMultiplexer _redis;
    private readonly SpotifyBatchQueueHelper _batchHelper;
    private readonly IRateLimitTracker _rateLimitTracker;
    private readonly ISagaStateManager _sagaManager;
    private readonly SpotifyLookupService _lookupService;
    private readonly ILogger<SpotifyBulkProcessorService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string SagaCompletedChannel = "saga:completed";
    private const string LookupCompleteChannelPrefix = "complete:";
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromMilliseconds( 500 );
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromSeconds( 5 );

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyBulkProcessorService"/> class.
    /// </summary>
    public SpotifyBulkProcessorService(
        IConnectionMultiplexer redis,
        SpotifyBatchQueueHelper batchHelper,
        IRateLimitTracker rateLimitTracker,
        ISagaStateManager sagaManager,
        SpotifyLookupService lookupService,
        ILogger<SpotifyBulkProcessorService> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _batchHelper = batchHelper ?? throw new ArgumentNullException( nameof( batchHelper ) );
        _rateLimitTracker = rateLimitTracker ?? throw new ArgumentNullException( nameof( rateLimitTracker ) );
        _sagaManager = sagaManager ?? throw new ArgumentNullException( nameof( sagaManager ) );
        _lookupService = lookupService ?? throw new ArgumentNullException( nameof( lookupService ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        _logger.LogInformation( "Spotify bulk processor service starting" );

        // Ensure consumer groups exist for type-specific streams
        await _batchHelper.EnsureConsumerGroupsAsync( stoppingToken );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                // Check if we should process bulk track lookups
                if (await ShouldProcessBulkTracksAsync( stoppingToken )) {
                    await ProcessBulkTrackLookupsAsync( stoppingToken );
                }

                // Check if we should process bulk album lookups
                if (await ShouldProcessBulkAlbumsAsync( stoppingToken )) {
                    await ProcessBulkAlbumLookupsAsync( stoppingToken );
                }

                // Wait before checking again
                await Task.Delay( s_checkInterval, stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error in bulk processor loop" );
                await Task.Delay( s_errorDelay, stoppingToken );
            }
        }

        _logger.LogInformation( "Spotify bulk processor service stopping" );
    }

    /// <summary>
    /// Checks if bulk track lookups should be processed.
    /// </summary>
    private async Task<bool> ShouldProcessBulkTracksAsync( CancellationToken ct ) {
        // Check if the bulk tracks endpoint is rate-limited
        RateLimitState rateLimitState = await _rateLimitTracker.GetStateAsync(
            SupportedProviders.Spotify,
            SpotifyConstants.BulkTracksEndpoint,
            ct
        );

        if (rateLimitState.IsRateLimited) {
            _logger.LogDebug(
                "Bulk tracks endpoint is rate-limited until {RetryAfter}",
                rateLimitState.RetryAfter
            );
            return false;
        }

        return await _batchHelper.ShouldBatchTrackLookupsAsync( ct );
    }

    /// <summary>
    /// Checks if bulk album lookups should be processed.
    /// </summary>
    private async Task<bool> ShouldProcessBulkAlbumsAsync( CancellationToken ct ) {
        // Check if the bulk albums endpoint is rate-limited
        RateLimitState rateLimitState = await _rateLimitTracker.GetStateAsync(
            SupportedProviders.Spotify,
            SpotifyConstants.BulkAlbumsEndpoint,
            ct
        );

        if (rateLimitState.IsRateLimited) {
            _logger.LogDebug(
                "Bulk albums endpoint is rate-limited until {RetryAfter}",
                rateLimitState.RetryAfter
            );
            return false;
        }

        return await _batchHelper.ShouldBatchAlbumLookupsAsync( ct );
    }

    /// <summary>
    /// Processes a batch of track ID lookups.
    /// </summary>
    private async Task ProcessBulkTrackLookupsAsync( CancellationToken ct ) {
        _logger.LogInformation( "Processing bulk track lookups" );

        // Collect messages from type-specific bulk stream first
        List<QueuedMessage<QueuedLookupRequest>> messages = [];
        messages.AddRange( await _batchHelper.DequeueTrackIdBatchAsync( SpotifyConstants.MaxTracksPerBatchLookup, ct ) );

        // If we don't have enough from bulk stream, collect from priority streams
        if (messages.Count < SpotifyConstants.MaxTracksPerBatchLookup) {
            int remaining = SpotifyConstants.MaxTracksPerBatchLookup - messages.Count;
            IReadOnlyList<QueuedMessage<QueuedLookupRequest>> fromPriority =
                await _batchHelper.CollectIdLookupsFromPriorityStreamsAsync( LookupRequestType.SongIdLookup, remaining, ct );
            messages.AddRange( fromPriority );
        }

        if (messages.Count == 0) {
            _logger.LogDebug( "No track ID lookups to process" );
            return;
        }

        _logger.LogInformation( "Processing {Count} track ID lookups in bulk", messages.Count );

        // Extract track IDs and create a mapping
        Dictionary<string, List<QueuedMessage<QueuedLookupRequest>>> idToMessages = [];
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            string trackId = message.Payload.LookupValue;
            if (!idToMessages.ContainsKey( trackId )) {
                idToMessages[trackId] = [];
            }
            idToMessages[trackId].Add( message );
        }

        try {
            // Call the bulk lookup API
            Dictionary<string, MusicLookupResult?> results = await _lookupService.GetTracksByIdsAsync( idToMessages.Keys );

            // Process results
            foreach (KeyValuePair<string, List<QueuedMessage<QueuedLookupRequest>>> kvp in idToMessages) {
                string trackId = kvp.Key;
                bool hasResult = results.TryGetValue( trackId, out MusicLookupResult? result );

                foreach (QueuedMessage<QueuedLookupRequest> message in kvp.Value) {
                    await ProcessBulkResultAsync( message, hasResult ? result : null, ct );
                }
            }

            _logger.LogInformation( "Successfully processed {Count} track ID lookups", messages.Count );
        } catch (RetryAfterExceededException ex) {
            await HandleBulkRateLimitAsync( messages, SpotifyConstants.BulkTracksEndpoint, ex, ct );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error processing bulk track lookups" );
            await RequeueAllAsync( messages, ct );
        }
    }

    /// <summary>
    /// Processes a batch of album ID lookups.
    /// </summary>
    private async Task ProcessBulkAlbumLookupsAsync( CancellationToken ct ) {
        _logger.LogInformation( "Processing bulk album lookups" );

        // Collect messages from type-specific bulk stream first
        List<QueuedMessage<QueuedLookupRequest>> messages = [];
        messages.AddRange( await _batchHelper.DequeueAlbumIdBatchAsync( SpotifyConstants.MaxAlbumsPerBatchLookup, ct ) );

        // If we don't have enough from bulk stream, collect from priority streams
        if (messages.Count < SpotifyConstants.MaxAlbumsPerBatchLookup) {
            int remaining = SpotifyConstants.MaxAlbumsPerBatchLookup - messages.Count;
            IReadOnlyList<QueuedMessage<QueuedLookupRequest>> fromPriority =
                await _batchHelper.CollectIdLookupsFromPriorityStreamsAsync( LookupRequestType.AlbumIdLookup, remaining, ct );
            messages.AddRange( fromPriority );
        }

        if (messages.Count == 0) {
            _logger.LogDebug( "No album ID lookups to process" );
            return;
        }

        _logger.LogInformation( "Processing {Count} album ID lookups in bulk", messages.Count );

        // Extract album IDs and create a mapping
        Dictionary<string, List<QueuedMessage<QueuedLookupRequest>>> idToMessages = [];
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            string albumId = message.Payload.LookupValue;
            if (!idToMessages.ContainsKey( albumId )) {
                idToMessages[albumId] = [];
            }
            idToMessages[albumId].Add( message );
        }

        try {
            // Call the bulk lookup API
            Dictionary<string, MusicLookupResult?> results = await _lookupService.GetAlbumsByIdsAsync( idToMessages.Keys );

            // Process results
            foreach (KeyValuePair<string, List<QueuedMessage<QueuedLookupRequest>>> kvp in idToMessages) {
                string albumId = kvp.Key;
                bool hasResult = results.TryGetValue( albumId, out MusicLookupResult? result );

                foreach (QueuedMessage<QueuedLookupRequest> message in kvp.Value) {
                    await ProcessBulkResultAsync( message, hasResult ? result : null, ct );
                }
            }

            _logger.LogInformation( "Successfully processed {Count} album ID lookups", messages.Count );
        } catch (RetryAfterExceededException ex) {
            await HandleBulkRateLimitAsync( messages, SpotifyConstants.BulkAlbumsEndpoint, ex, ct );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error processing bulk album lookups" );
            await RequeueAllAsync( messages, ct );
        }
    }

    /// <summary>
    /// Processes the result for a single message from a bulk lookup.
    /// </summary>
    private async Task ProcessBulkResultAsync(
        QueuedMessage<QueuedLookupRequest> message,
        MusicLookupResult? result,
        CancellationToken ct
    ) {
        QueuedLookupRequest request = message.Payload;

        try {
            // Update saga state with result
            await _sagaManager.UpdateProviderStateAsync(
                request.SagaId,
                new ProviderLookupState(
                    Provider: SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: result is not null,
                    ResultJson: result is not null ? JsonSerializer.Serialize( result, _jsonOptions ) : null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: result is null ? "Not found" : null
                ),
                ct
            );

            // Publish lookup completion
            await PublishLookupCompletionAsync( request.SagaId, ct );

            // Check if saga is complete
            await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );

            // Acknowledge the message
            await _batchHelper.AcknowledgeAsync( message.MessageId, ct );

            _logger.LogDebug(
                "Processed bulk result for saga {SagaId}: {Result}",
                request.SagaId,
                result is not null ? "found" : "not found"
            );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error processing bulk result for saga {SagaId}", request.SagaId );
            await _batchHelper.RequeueAsync( message.MessageId, ct );
        }
    }

    /// <summary>
    /// Handles rate limiting for bulk operations.
    /// </summary>
    private async Task HandleBulkRateLimitAsync(
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages,
        string endpoint,
        RetryAfterExceededException ex,
        CancellationToken ct
    ) {
        _logger.LogWarning(
            "Rate limit encountered on bulk endpoint {Endpoint}, retry after {RetryAfter}",
            endpoint,
            ex.RetryAfterValue
        );

        // Record the rate limit state
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.Add( ex.RetryAfterValue );
        await _rateLimitTracker.SetRateLimitedAsync( SupportedProviders.Spotify, endpoint, retryAfter, ct );

        // Requeue all messages
        await RequeueAllAsync( messages, ct );
    }

    /// <summary>
    /// Requeues all messages in a batch.
    /// </summary>
    private async Task RequeueAllAsync(
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages,
        CancellationToken ct
    ) {
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            try {
                await _batchHelper.RequeueAsync( message.MessageId, ct );
            } catch (Exception ex) {
                _logger.LogError( ex, "Error requeuing message {MessageId}", message.MessageId );
            }
        }
    }

    /// <summary>
    /// Checks if a saga is complete and publishes a completion event.
    /// </summary>
    private async Task CheckAndPublishSagaCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null || !saga.IsComplete || !string.IsNullOrEmpty( saga.FinalResultUri )) {
                return;
            }

            _logger.LogInformation( "Saga {SagaId} is complete, publishing completion event", sagaId );

            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( SagaCompletedChannel ), sagaId );
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to check/publish saga completion for {SagaId}", sagaId );
        }
    }

    /// <summary>
    /// Publishes a lookup completion event so waiting clients can receive results.
    /// </summary>
    private async Task PublishLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                return;
            }

            string channel = $"{LookupCompleteChannelPrefix}{saga.LookupKey}";
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync(
                RedisChannel.Literal( channel ),
                saga.PartialResultUri ?? saga.FinalResultUri ?? string.Empty
            );
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to publish lookup completion for {SagaId}", sagaId );
        }
    }
}
