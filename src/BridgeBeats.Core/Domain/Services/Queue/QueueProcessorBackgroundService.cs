using System.Diagnostics;
using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace BridgeBeats.Core.Domain.Services.Queue;

/// <summary>
/// Background service that consumes lookup requests from a provider-specific Redis stream
/// and processes them using the provider's lookup service.
/// </summary>
/// <remarks>
/// <para>
/// Each provider worker hosts an instance of this service configured for its specific provider.
/// The service:
/// <list type="bullet">
///   <item>Reads messages from the provider's Redis stream using consumer groups</item>
///   <item>Checks rate limit state before processing</item>
///   <item>Calls the provider's lookup service to perform the lookup</item>
///   <item>Updates saga state with results</item>
///   <item>Acknowledges or requeues messages based on outcome</item>
///   <item>Publishes saga completion events for the coordinator</item>
/// </list>
/// </para>
/// <para>
/// When a lookup encounters a rate limit exception, the request is requeued with
/// a delay matching the Retry-After period. The rate limit is also recorded for
/// the specific endpoint to prevent other requests from immediately hitting the same limit.
/// </para>
/// </remarks>
public sealed partial class QueueProcessorBackgroundService : BackgroundService {
    private readonly IConnectionMultiplexer _redis;
    private readonly IRequestQueue<QueuedLookupRequest> _queue;
    private readonly IRateLimitTracker _rateLimitTracker;
    private readonly ISagaStateManager _sagaManager;
    private readonly IMusicLookupService _lookupService;
    private readonly SupportedProviders _provider;
    private readonly ILogger<QueueProcessorBackgroundService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private const int MaxRetryAttempts = 5;
    private const string SagaCompletedChannel = "saga:completed";
    private const string LookupCompleteChannelPrefix = "complete:";
    private static readonly TimeSpan s_noMessageDelay = TimeSpan.FromMilliseconds( 100 );
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromSeconds( 1 );

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueProcessorBackgroundService"/> class.
    /// </summary>
    /// <param name="redis">Redis connection for publishing saga completion events.</param>
    /// <param name="queue">The provider-specific request queue.</param>
    /// <param name="rateLimitTracker">The rate limit tracker for checking endpoint availability.</param>
    /// <param name="sagaManager">The saga state manager for tracking multi-provider lookups.</param>
    /// <param name="lookupService">The provider's music lookup service.</param>
    /// <param name="provider">The provider this service processes requests for.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public QueueProcessorBackgroundService(
        IConnectionMultiplexer redis,
        IRequestQueue<QueuedLookupRequest> queue,
        IRateLimitTracker rateLimitTracker,
        ISagaStateManager sagaManager,
        IMusicLookupService lookupService,
        SupportedProviders provider,
        ILogger<QueueProcessorBackgroundService> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _queue = queue ?? throw new ArgumentNullException( nameof( queue ) );
        _rateLimitTracker = rateLimitTracker ?? throw new ArgumentNullException( nameof( rateLimitTracker ) );
        _sagaManager = sagaManager ?? throw new ArgumentNullException( nameof( sagaManager ) );
        _lookupService = lookupService ?? throw new ArgumentNullException( nameof( lookupService ) );
        _provider = provider;
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogQueueProcessorStarting( _logger, _provider );

        // Ensure consumer groups exist before starting to consume
        if (_queue is RedisRequestQueue<QueuedLookupRequest> redisQueue) {
            await redisQueue.EnsureConsumerGroupsAsync( stoppingToken );
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                // Use rate-limit-aware dequeue to skip messages for blocked endpoints
                QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( _rateLimitTracker, stoppingToken );

                if (message is null) {
                    // No messages available, wait briefly before checking again
                    await Task.Delay( s_noMessageDelay, stoppingToken );
                    continue;
                }

                await ProcessMessageAsync( message, stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // Normal shutdown
                break;
            } catch (Exception ex) {
                LogQueueProcessorLoopError( _logger, ex, _provider );
                await Task.Delay( s_errorDelay, stoppingToken );
            }
        }

        LogQueueProcessorStopping( _logger, _provider );
    }

    private async Task ProcessMessageAsync( QueuedMessage<QueuedLookupRequest> message, CancellationToken ct ) {
        QueuedLookupRequest request = message.Payload;
        Stopwatch stopwatch = Stopwatch.StartNew( );
        string status = "success";

        LogProcessingMessage( _logger, message.MessageId, request.SagaId, request.LookupType );

        // Ensure saga exists before processing. This handles JetStream bulk lookups where
        // the saga ID is set but the saga hasn't been created yet (fire-and-forget pattern).
        // For web lookups, this will return the existing saga.
        _ = await _sagaManager.GetOrCreateAsync(
            request.SagaId,
            $"{request.LookupType}:{request.LookupValue}",
            request.LookupType,
            request.LookupValue,
            request.OriginPriority,
            ct
        );

        // Initialize provider state for this provider if not already done
        await _sagaManager.InitializeProviderStatesAsync( request.SagaId, [_provider], ct );

        // Check if the endpoint for this lookup type is rate-limited
        string endpoint = request.LookupType.ToString( );
        RateLimitState rateLimitState = await _rateLimitTracker.GetStateAsync( _provider, endpoint, ct );

        if (rateLimitState.IsRateLimited) {
            if (_logger.IsEnabled( LogLevel.Debug )) {
                DateTimeOffset retryAfter = rateLimitState.RetryAfter.GetValueOrDefault( );
                LogEndpointRateLimited( _logger, endpoint, retryAfter, message.MessageId );
            }

            // Requeue with delay until rate limit expires
            await _queue.RequeueAsync( message.MessageId, rateLimitState.TimeRemaining, ct );

            // Record processing duration with rate_limited status
            stopwatch.Stop( );
            QueueMetrics.RecordProcessingDuration( _provider, request.LookupType, "rate_limited", stopwatch.Elapsed.TotalSeconds );
            return;
        }

        try {
            // Perform the lookup
            MusicLookupResult? result = await PerformLookupAsync( request, ct );

            // Update saga state with successful result
            await _sagaManager.UpdateProviderStateAsync(
                request.SagaId,
                new ProviderLookupState(
                    Provider: _provider,
                    IsComplete: true,
                    IsSuccess: result is not null,
                    ResultJson: result is not null ? JsonSerializer.Serialize( result, _jsonOptions ) : null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: null
                ),
                ct
            );

            // Check if saga is complete and publish saga completion event for coordinator
            await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );

            // Publish lookup completion so the SagaCoordinator can process via pattern subscription
            await PublishLookupCompletionAsync( request.SagaId, ct );

            // Acknowledge the message - processing complete
            await _queue.AcknowledgeAsync( message.MessageId, ct );

            LogMessageProcessed( _logger, message.MessageId, request.SagaId );
        } catch (RetryAfterExceededException ex) {
            status = "rate_limited";
            await HandleRateLimitExceptionAsync( message, request, ex, ct );
        } catch (Exception ex) {
            status = "failure";
            await HandleProcessingExceptionAsync( message, request, ex, ct );
        } finally {
            stopwatch.Stop( );
            QueueMetrics.RecordProcessingDuration( _provider, request.LookupType, status, stopwatch.Elapsed.TotalSeconds );
        }
    }

    private async Task<MusicLookupResult?> PerformLookupAsync( QueuedLookupRequest request, CancellationToken ct ) {
        // Check for cancellation before performing the lookup
        ct.ThrowIfCancellationRequested( );

        // Map LookupRequestType to the appropriate lookup method
        return request.LookupType switch {
            LookupRequestType.UriLookup => await _lookupService.GetInfoAsync( request.LookupValue ),
            LookupRequestType.IsrcLookup => await _lookupService.GetInfoByISRCAsync( request.LookupValue ),
            LookupRequestType.UpcLookup => await _lookupService.GetInfoByUPCAsync( request.LookupValue ),
            LookupRequestType.SongIdLookup => await _lookupService.GetInfoByIDAsync( request.LookupValue, false ),
            LookupRequestType.AlbumIdLookup => await _lookupService.GetInfoByIDAsync( request.LookupValue, true ),
            LookupRequestType.ArtistLookup when request is { Title: not null, Artist: not null } =>
                await _lookupService.GetInfoAsync( request.Title, request.Artist ),
            LookupRequestType.SongLookup when request is { Title: not null, Artist: not null } =>
                await _lookupService.GetInfoAsync( request.Title, request.Artist ),
            LookupRequestType.AlbumLookup when request is { Title: not null, Artist: not null } =>
                await _lookupService.GetInfoAsync( request.Title, request.Artist ),
            LookupRequestType.ArtistAlbumLookup => throw new NotImplementedException( ),
            LookupRequestType.AlbumTrackLookup => throw new NotImplementedException( ),
            _ => throw new InvalidOperationException(
                            $"Unsupported lookup type {request.LookupType} or missing required parameters"
                        )
        };
    }

    private async Task HandleRateLimitExceptionAsync(
        QueuedMessage<QueuedLookupRequest> message,
        QueuedLookupRequest request,
        RetryAfterExceededException ex,
        CancellationToken ct
    ) {
        // Use the LookupRequestType as the endpoint identifier
        string endpoint = request.LookupType.ToString( );

        LogRateLimitEncountered( _logger, _provider, endpoint, ex.RetryAfterValue );

        // Record the rate limit state
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.Add( ex.RetryAfterValue );
        await _rateLimitTracker.SetRateLimitedAsync( _provider, endpoint, retryAfter, ct );

        // Mark saga as partial and record rate limit info for user notification
        await _sagaManager.SetIsPartialAsync( request.SagaId, true, ct );

        // Merge with rate limit info recorded by other providers so no entry is lost -
        // the orchestrator's all-pending-providers-rate-limited escape hatch depends on
        // every rate-limited provider being present. This is a read-modify-write (the
        // saga manager has no atomic merge); a lost update under concurrent writes only
        // degrades to the previous overwrite behavior.
        LookupSagaState? sagaForRateLimit = await _sagaManager.GetAsync( request.SagaId, ct );
        List<ProviderRateLimitInfo> mergedRateLimitInfo = sagaForRateLimit?.RateLimitInfo is not null
            ? [.. sagaForRateLimit.RateLimitInfo.Where( r => r.Provider != _provider )]
            : [];
        mergedRateLimitInfo.Add( new ProviderRateLimitInfo( _provider, retryAfter, endpoint ) );

        await _sagaManager.SetRateLimitInfoAsync( request.SagaId, mergedRateLimitInfo, ct );

        // Publish rate-limited sentinel so waiting clients know this is a partial result
        await PublishRateLimitSentinelAsync( request.SagaId, ct );

        // Requeue request - it will be skipped by rate-limit-aware dequeue until rate limit expires
        // The RateLimitedEndpoint field helps track which endpoint triggered the limit
        QueuedLookupRequest requeuedRequest = request with {
            AttemptCount = request.AttemptCount + 1,
            RateLimitedEndpoint = endpoint
        };

        await _queue.AcknowledgeAsync( message.MessageId, ct );
        await _queue.EnqueueAsync( requeuedRequest, QueuePriority.Background, ct );

        LogSagaMarkedPartial( _logger, request.SagaId, endpoint, _provider, retryAfter );
    }

    private async Task HandleProcessingExceptionAsync(
        QueuedMessage<QueuedLookupRequest> message,
        QueuedLookupRequest request,
        Exception ex,
        CancellationToken ct
    ) {
        LogProcessingFailed( _logger, ex, request.RequestId, request.SagaId );

        // Check if we've exceeded retry attempts
        if (request.AttemptCount >= MaxRetryAttempts) {
            LogMaxRetriesExceeded( _logger, message.MessageId, MaxRetryAttempts );

            // Update saga with error state
            await _sagaManager.UpdateProviderStateAsync(
                request.SagaId,
                new ProviderLookupState(
                    Provider: _provider,
                    IsComplete: true,
                    IsSuccess: false,
                    ResultJson: null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: $"Failed after {MaxRetryAttempts} attempts: {ex.Message}"
                ),
                ct
            );

            // Move to Dead Letter Queue
            await _queue.MoveToDlqAsync( message.MessageId, ex.Message, ct );

            // Check if saga is complete and publish completion event
            await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );
        } else {
            // Update saga with error state but don't mark as complete
            await _sagaManager.UpdateProviderStateAsync(
                request.SagaId,
                new ProviderLookupState(
                    Provider: _provider,
                    IsComplete: true,
                    IsSuccess: false,
                    ResultJson: null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: ex.Message
                ),
                ct
            );

            // Check if saga is complete and publish completion event
            await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );

            // Acknowledge the message (don't retry indefinitely for non-rate-limit errors)
            await _queue.AcknowledgeAsync( message.MessageId, ct );
        }
    }

    private async Task CheckAndPublishSagaCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                LogSagaNotFoundForCompletion( _logger, sagaId );
                return;
            }

            if (!saga.IsComplete) {
                LogSagaNotYetComplete( _logger, sagaId );
                return;
            }

            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                LogSagaAlreadyHasFinalResult( _logger, sagaId );
                return;
            }

            // Saga is complete but doesn't have a final result - publish completion event
            LogSagaCompletePublishing( _logger, sagaId );
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( SagaCompletedChannel ), sagaId );
        } catch (Exception ex) {
            LogSagaCompletionCheckFailed( _logger, ex, sagaId );
        }
    }

    private async Task PublishLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            // Get the saga to retrieve the lookup key for the completion channel
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                LogSagaNotFoundForLookupCompletion( _logger, sagaId );
                return;
            }

            // Publish current saga result URI (may be empty for in-progress lookups)
            // Empty publishes trigger the SagaCoordinator's pattern subscription
            // while WaitForCompletionAsync skips them and waits for a real result
            string resultUri = saga.PartialResultUri ?? saga.FinalResultUri ?? string.Empty;
            string channel = $"{LookupCompleteChannelPrefix}{saga.LookupKey}";
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( channel ), resultUri );

            LogLookupCompletionPublished( _logger, sagaId, channel );
        } catch (Exception ex) {
            LogLookupCompletionPublishFailed( _logger, ex, sagaId );
        }
    }

    private async Task PublishRateLimitSentinelAsync( string sagaId, CancellationToken ct ) {
        try {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                LogSagaNotFoundForLookupCompletion( _logger, sagaId );
                return;
            }

            // Publish rate-limited sentinel so waiting clients know this is a partial result
            string channel = $"{LookupCompleteChannelPrefix}{saga.LookupKey}";
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( channel ), LookupConstants.RateLimitedSentinel );

            LogLookupCompletionPublished( _logger, sagaId, channel );
        } catch (Exception ex) {
            LogLookupCompletionPublishFailed( _logger, ex, sagaId );
        }
    }

    #region LoggerMessage Definitions

    /// <summary>
    /// Logs that the queue processor is starting for a provider.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.QueueProcessorStarting,
        Level = LogLevel.Information,
        Message = "Queue processor starting for provider {Provider}" )]
    private static partial void LogQueueProcessorStarting( ILogger logger, SupportedProviders provider );

    /// <summary>
    /// Logs that the queue processor is stopping for a provider.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.QueueProcessorStopping,
        Level = LogLevel.Information,
        Message = "Queue processor stopping for provider {Provider}" )]
    private static partial void LogQueueProcessorStopping( ILogger logger, SupportedProviders provider );

    /// <summary>
    /// Logs an error in the queue processing loop.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.QueueProcessorLoopError,
        Level = LogLevel.Error,
        Message = "Error in queue processing loop for {Provider}" )]
    private static partial void LogQueueProcessorLoopError( ILogger logger, Exception ex, SupportedProviders provider );

    /// <summary>
    /// Logs that a message is being processed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.ProcessingMessage,
        Level = LogLevel.Debug,
        Message = "Processing message {MessageId} for saga {SagaId}, lookup type {LookupType}" )]
    private static partial void LogProcessingMessage( ILogger logger, string messageId, string sagaId, LookupRequestType lookupType );

    /// <summary>
    /// Logs that an endpoint is rate-limited and the message is being requeued.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.EndpointRateLimited,
        Level = LogLevel.Debug,
        Message = "Endpoint {Endpoint} is rate-limited until {RetryAfter}, requeuing message {MessageId}" )]
    private static partial void LogEndpointRateLimited( ILogger logger, string endpoint, DateTimeOffset retryAfter, string messageId );

    /// <summary>
    /// Logs that a message was successfully processed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.MessageProcessed,
        Level = LogLevel.Debug,
        Message = "Successfully processed message {MessageId} for saga {SagaId}" )]
    private static partial void LogMessageProcessed( ILogger logger, string messageId, string sagaId );

    /// <summary>
    /// Logs that a rate limit was encountered for a provider endpoint.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.RateLimitEncountered,
        Level = LogLevel.Warning,
        Message = "Rate limit encountered for {Provider} endpoint {Endpoint}, retry after {RetryAfter}" )]
    private static partial void LogRateLimitEncountered( ILogger logger, SupportedProviders provider, string endpoint, TimeSpan retryAfter );

    /// <summary>
    /// Logs that a saga was marked as partial due to rate limiting.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaMarkedPartial,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} marked as partial due to rate limit on {Endpoint}, requeued for {Provider} (will be deferred until {RetryAfter})" )]
    private static partial void LogSagaMarkedPartial( ILogger logger, string sagaId, string endpoint, SupportedProviders provider, DateTimeOffset retryAfter );

    /// <summary>
    /// Logs that processing of a lookup request failed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.ProcessingFailed,
        Level = LogLevel.Error,
        Message = "Failed to process lookup request {RequestId} for saga {SagaId}" )]
    private static partial void LogProcessingFailed( ILogger logger, Exception ex, string requestId, string sagaId );

    /// <summary>
    /// Logs that a message exceeded the maximum retry attempts.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.MaxRetriesExceeded,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} exceeded max retry attempts ({MaxRetries}), moving to DLQ" )]
    private static partial void LogMaxRetriesExceeded( ILogger logger, string messageId, int maxRetries );

    /// <summary>
    /// Logs that a saga was not found when checking for completion.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaNotFoundForCompletion,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} not found, cannot check completion" )]
    private static partial void LogSagaNotFoundForCompletion( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that a saga is not yet complete.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaNotYetComplete,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} is not yet complete" )]
    private static partial void LogSagaNotYetComplete( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that a saga already has a final result.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaAlreadyHasFinalResult,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} already has final result" )]
    private static partial void LogSagaAlreadyHasFinalResult( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that a saga is complete and a completion event is being published.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaCompletePublishing,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} is complete, publishing completion event" )]
    private static partial void LogSagaCompletePublishing( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that checking/publishing saga completion failed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaCompletionCheckFailed,
        Level = LogLevel.Error,
        Message = "Failed to check/publish saga completion for {SagaId}" )]
    private static partial void LogSagaCompletionCheckFailed( ILogger logger, Exception ex, string sagaId );

    /// <summary>
    /// Logs that a saga was not found when publishing lookup completion.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaNotFoundForLookupCompletion,
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} not found, cannot publish lookup completion" )]
    private static partial void LogSagaNotFoundForLookupCompletion( ILogger logger, string sagaId );

    /// <summary>
    /// Logs that a lookup completion was published.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.LookupCompletionPublished,
        Level = LogLevel.Debug,
        Message = "Published lookup completion for saga {SagaId} on channel {Channel}" )]
    private static partial void LogLookupCompletionPublished( ILogger logger, string sagaId, string channel );

    /// <summary>
    /// Logs that publishing a lookup completion failed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.LookupCompletionPublishFailed,
        Level = LogLevel.Error,
        Message = "Failed to publish lookup completion for {SagaId}" )]
    private static partial void LogLookupCompletionPublishFailed( ILogger logger, Exception ex, string sagaId );

    #endregion LoggerMessage Definitions
}
