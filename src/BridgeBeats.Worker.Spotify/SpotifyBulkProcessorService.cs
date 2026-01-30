using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Worker.Spotify.Logging;
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
public sealed partial class SpotifyBulkProcessorService : BackgroundService {

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
        LogServiceStarting( _logger );

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
                LogProcessorLoopError( _logger, ex );
                await Task.Delay( s_errorDelay, stoppingToken );
            }
        }

        LogServiceStopping( _logger );
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
            LogRateLimited( _logger, "Bulk tracks", rateLimitState.RetryAfter?.ToString( ) ?? "unknown" );
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
            LogRateLimited( _logger, "Bulk albums", rateLimitState.RetryAfter?.ToString( ) ?? "unknown" );
            return false;
        }

        return await _batchHelper.ShouldBatchAlbumLookupsAsync( ct );
    }

    /// <summary>
    /// Processes a batch of track ID lookups.
    /// </summary>
    private async Task ProcessBulkTrackLookupsAsync( CancellationToken ct ) {
        LogProcessingBulkTracks( _logger );

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
            LogNoTrackLookups( _logger );
            return;
        }

        LogProcessingTrackCount( _logger, messages.Count );

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

            LogTrackLookupsSuccess( _logger, messages.Count );
        } catch (RetryAfterExceededException ex) {
            await HandleBulkRateLimitAsync( messages, SpotifyConstants.BulkTracksEndpoint, ex, ct );
        } catch (Exception ex) {
            LogTrackLookupsError( _logger, ex );
            await RequeueAllAsync( messages, ct );
        }
    }

    /// <summary>
    /// Processes a batch of album ID lookups.
    /// </summary>
    private async Task ProcessBulkAlbumLookupsAsync( CancellationToken ct ) {
        LogProcessingBulkAlbums( _logger );

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
            LogNoAlbumLookups( _logger );
            return;
        }

        LogProcessingAlbumCount( _logger, messages.Count );

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

            LogAlbumLookupsSuccess( _logger, messages.Count );
        } catch (RetryAfterExceededException ex) {
            await HandleBulkRateLimitAsync( messages, SpotifyConstants.BulkAlbumsEndpoint, ex, ct );
        } catch (Exception ex) {
            LogAlbumLookupsError( _logger, ex );
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

            LogBulkResultProcessed( _logger, request.SagaId, result is not null ? "found" : "not found" );
        } catch (Exception ex) {
            LogBulkResultError( _logger, ex, request.SagaId );
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
        LogRateLimitEncountered( _logger, endpoint, ex.RetryAfterValue.ToString( ) );

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
                LogRequeueError( _logger, ex, message.MessageId );
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

            LogSagaComplete( _logger, sagaId );

            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( SagaCompletedChannel ), sagaId );
        } catch (Exception ex) {
            LogSagaCompletionCheckError( _logger, ex, sagaId );
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
            LogPublishCompletionError( _logger, ex, sagaId );
        }
    }

    #region LoggerMessage Methods

    /// <summary>Logs that the service is starting.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkProcessorStarting,
        Level = LogLevel.Information,
        Message = "Spotify bulk processor service starting" )]
    private static partial void LogServiceStarting( ILogger logger );

    /// <summary>Logs an error in the processor loop.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkProcessorLoopError,
        Level = LogLevel.Error,
        Message = "Error in bulk processor loop" )]
    private static partial void LogProcessorLoopError( ILogger logger, Exception ex );

    /// <summary>Logs that the service is stopping.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkProcessorStopping,
        Level = LogLevel.Information,
        Message = "Spotify bulk processor service stopping" )]
    private static partial void LogServiceStopping( ILogger logger );

    /// <summary>Logs that an endpoint is rate limited.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkRateLimited,
        Level = LogLevel.Debug,
        Message = "{Endpoint} endpoint is rate-limited until {RetryAfter}" )]
    private static partial void LogRateLimited( ILogger logger, string endpoint, string retryAfter );

    /// <summary>Logs that bulk track lookups are being processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingBulkTracks,
        Level = LogLevel.Information,
        Message = "Processing bulk track lookups" )]
    private static partial void LogProcessingBulkTracks( ILogger logger );

    /// <summary>Logs that there are no track lookups to process.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoTrackLookups,
        Level = LogLevel.Debug,
        Message = "No track ID lookups to process" )]
    private static partial void LogNoTrackLookups( ILogger logger );

    /// <summary>Logs the number of track lookups being processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingTrackCount,
        Level = LogLevel.Information,
        Message = "Processing {Count} track ID lookups in bulk" )]
    private static partial void LogProcessingTrackCount( ILogger logger, int count );

    /// <summary>Logs that track lookups were successful.</summary>
    [LoggerMessage(
        EventId = LogEventIds.TrackLookupsSuccess,
        Level = LogLevel.Information,
        Message = "Successfully processed {Count} track ID lookups" )]
    private static partial void LogTrackLookupsSuccess( ILogger logger, int count );

    /// <summary>Logs an error processing track lookups.</summary>
    [LoggerMessage(
        EventId = LogEventIds.TrackLookupsError,
        Level = LogLevel.Error,
        Message = "Error processing bulk track lookups" )]
    private static partial void LogTrackLookupsError( ILogger logger, Exception ex );

    /// <summary>Logs that bulk album lookups are being processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingBulkAlbums,
        Level = LogLevel.Information,
        Message = "Processing bulk album lookups" )]
    private static partial void LogProcessingBulkAlbums( ILogger logger );

    /// <summary>Logs that there are no album lookups to process.</summary>
    [LoggerMessage(
        EventId = LogEventIds.NoAlbumLookups,
        Level = LogLevel.Debug,
        Message = "No album ID lookups to process" )]
    private static partial void LogNoAlbumLookups( ILogger logger );

    /// <summary>Logs the number of album lookups being processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingAlbumCount,
        Level = LogLevel.Information,
        Message = "Processing {Count} album ID lookups in bulk" )]
    private static partial void LogProcessingAlbumCount( ILogger logger, int count );

    /// <summary>Logs that album lookups were successful.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AlbumLookupsSuccess,
        Level = LogLevel.Information,
        Message = "Successfully processed {Count} album ID lookups" )]
    private static partial void LogAlbumLookupsSuccess( ILogger logger, int count );

    /// <summary>Logs an error processing album lookups.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AlbumLookupsError,
        Level = LogLevel.Error,
        Message = "Error processing bulk album lookups" )]
    private static partial void LogAlbumLookupsError( ILogger logger, Exception ex );

    /// <summary>Logs that a bulk result was processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkResultProcessed,
        Level = LogLevel.Debug,
        Message = "Processed bulk result for saga {SagaId}: {Result}" )]
    private static partial void LogBulkResultProcessed( ILogger logger, string sagaId, string result );

    /// <summary>Logs an error processing a bulk result.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkResultError,
        Level = LogLevel.Error,
        Message = "Error processing bulk result for saga {SagaId}" )]
    private static partial void LogBulkResultError( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs that a rate limit was encountered.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RateLimitEncountered,
        Level = LogLevel.Warning,
        Message = "Rate limit encountered on bulk endpoint {Endpoint}, retry after {RetryAfter}" )]
    private static partial void LogRateLimitEncountered( ILogger logger, string endpoint, string retryAfter );

    /// <summary>Logs an error requeuing a message.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RequeueError,
        Level = LogLevel.Error,
        Message = "Error requeuing message {MessageId}" )]
    private static partial void LogRequeueError( ILogger logger, Exception ex, string messageId );

    /// <summary>Logs that a saga is complete.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaComplete,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} is complete, publishing completion event" )]
    private static partial void LogSagaComplete( ILogger logger, string sagaId );

    /// <summary>Logs an error checking saga completion.</summary>
    [LoggerMessage(
        EventId = LogEventIds.SagaCompletionCheckError,
        Level = LogLevel.Error,
        Message = "Failed to check/publish saga completion for {SagaId}" )]
    private static partial void LogSagaCompletionCheckError( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs an error publishing lookup completion.</summary>
    [LoggerMessage(
        EventId = LogEventIds.PublishCompletionError,
        Level = LogLevel.Error,
        Message = "Failed to publish lookup completion for {SagaId}" )]
    private static partial void LogPublishCompletionError( ILogger logger, Exception ex, string sagaId );

    #endregion
}
