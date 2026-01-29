using System.Diagnostics;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
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
public sealed class QueueProcessorBackgroundService : BackgroundService {
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
        _logger.LogInformation(
            "Queue processor starting for provider {Provider}",
            _provider
        );

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
                _logger.LogError( ex, "Error in queue processing loop for {Provider}", _provider );
                await Task.Delay( s_errorDelay, stoppingToken );
            }
        }

        _logger.LogInformation( "Queue processor stopping for provider {Provider}", _provider );
    }

    private async Task ProcessMessageAsync( QueuedMessage<QueuedLookupRequest> message, CancellationToken ct ) {
        QueuedLookupRequest request = message.Payload;
        Stopwatch stopwatch = Stopwatch.StartNew( );
        string status = "success";

        _logger.LogDebug(
            "Processing message {MessageId} for saga {SagaId}, lookup type {LookupType}",
            message.MessageId,
            request.SagaId,
            request.LookupType
        );

        // Check if the endpoint for this lookup type is rate-limited
        string endpoint = request.LookupType.ToString( );
        RateLimitState rateLimitState = await _rateLimitTracker.GetStateAsync( _provider, endpoint, ct );

        if (rateLimitState.IsRateLimited) {
            _logger.LogDebug(
                "Endpoint {Endpoint} is rate-limited until {RetryAfter}, requeuing message {MessageId}",
                endpoint,
                rateLimitState.RetryAfter,
                message.MessageId
            );

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

            // Publish lookup completion so waiting clients can receive the result
            await PublishLookupCompletionAsync( request.SagaId, ct );

            // Check if saga is complete and publish saga completion event for coordinator
            await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );

            // Acknowledge the message - processing complete
            await _queue.AcknowledgeAsync( message.MessageId, ct );

            _logger.LogDebug(
                "Successfully processed message {MessageId} for saga {SagaId}",
                message.MessageId,
                request.SagaId
            );
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

        _logger.LogWarning(
            "Rate limit encountered for {Provider} endpoint {Endpoint}, retry after {RetryAfter}",
            _provider,
            endpoint,
            ex.RetryAfterValue
        );

        // Record the rate limit state
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.Add( ex.RetryAfterValue );
        await _rateLimitTracker.SetRateLimitedAsync( _provider, endpoint, retryAfter, ct );

        // Mark saga as partial and record rate limit info for user notification
        await _sagaManager.SetIsPartialAsync( request.SagaId, true, ct );
        await _sagaManager.SetRateLimitInfoAsync(
            request.SagaId,
            [new ProviderRateLimitInfo( _provider, retryAfter, endpoint )],
            ct
        );

        // Publish partial completion so waiting clients can receive partial results
        await PublishLookupCompletionAsync( request.SagaId, ct );

        // Requeue request - it will be skipped by rate-limit-aware dequeue until rate limit expires
        // The RateLimitedEndpoint field helps track which endpoint triggered the limit
        QueuedLookupRequest requeuedRequest = request with {
            AttemptCount = request.AttemptCount + 1,
            RateLimitedEndpoint = endpoint
        };

        await _queue.AcknowledgeAsync( message.MessageId, ct );
        await _queue.EnqueueAsync( requeuedRequest, QueuePriority.Background, ct );

        _logger.LogInformation(
            "Saga {SagaId} marked as partial due to rate limit on {Endpoint}, requeued for {Provider} (will be deferred until {RetryAfter})",
            request.SagaId,
            endpoint,
            _provider,
            retryAfter
        );
    }

    private async Task HandleProcessingExceptionAsync(
        QueuedMessage<QueuedLookupRequest> message,
        QueuedLookupRequest request,
        Exception ex,
        CancellationToken ct
    ) {
        _logger.LogError(
            ex,
            "Failed to process lookup request {RequestId} for saga {SagaId}",
            request.RequestId,
            request.SagaId
        );

        // Check if we've exceeded retry attempts
        if (request.AttemptCount >= MaxRetryAttempts) {
            _logger.LogWarning(
                "Message {MessageId} exceeded max retry attempts ({MaxRetries}), moving to DLQ",
                message.MessageId,
                MaxRetryAttempts
            );

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
                _logger.LogDebug( "Saga {SagaId} not found, cannot check completion", sagaId );
                return;
            }

            if (!saga.IsComplete) {
                _logger.LogDebug( "Saga {SagaId} is not yet complete", sagaId );
                return;
            }

            if (!string.IsNullOrEmpty( saga.FinalResultUri )) {
                _logger.LogDebug( "Saga {SagaId} already has final result", sagaId );
                return;
            }

            // Saga is complete but doesn't have a final result - publish completion event
            _logger.LogInformation( "Saga {SagaId} is complete, publishing completion event", sagaId );

            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( SagaCompletedChannel ), sagaId );
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to check/publish saga completion for {SagaId}", sagaId );
        }
    }

    private async Task PublishLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            // Get the saga to retrieve the lookup key for the completion channel
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                _logger.LogWarning( "Saga {SagaId} not found, cannot publish lookup completion", sagaId );
                return;
            }

            // Publish to the completion channel so waiting clients get notified
            string channel = $"{LookupCompleteChannelPrefix}{saga.LookupKey}";
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( channel ), saga.PartialResultUri ?? saga.FinalResultUri ?? string.Empty );

            _logger.LogDebug(
                "Published lookup completion for saga {SagaId} on channel {Channel}",
                sagaId,
                channel
            );
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to publish lookup completion for {SagaId}", sagaId );
        }
    }
}
