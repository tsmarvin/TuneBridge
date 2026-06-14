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
using BridgeBeats.Core.Infrastructure.Utilities;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace BridgeBeats.Core.Domain.Services.Queue;

/// <summary>
/// Worker-side queue consumer: one instance per provider drains that provider's request queue,
/// performs the actual provider lookup, records the outcome in the shared saga state, and publishes
/// completion so the orchestrator's waiters can wake. The saga state is the durable contract shared
/// with the lookup orchestrator on the web side.
/// </summary>
/// <remarks>
/// Per message the loop: resolves or creates the saga and initializes this provider's state;
/// pre-checks the endpoint rate limit and requeues if limited; performs the lookup; on success
/// records the serialized result, checks/publishes saga completion, publishes the per-lookup
/// completion, and acknowledges. A <see cref="RetryAfterExceededException"/> marks the saga partial,
/// merges this provider's rate-limit window, publishes the rate-limited sentinel, and re-enqueues at
/// background priority (interactive requests are explicitly deferred to the background lane). Other
/// exceptions retry up to <see cref="LookupConstants.MaxQueueRetryAttempts"/>, then move to the DLQ.
/// </remarks>
public sealed partial class QueueProcessorBackgroundService : BackgroundService {
    /// <summary>The Redis connection used to publish saga and lookup completion events.</summary>
    private readonly IConnectionMultiplexer _redis;
    /// <summary>This provider's request queue, drained for work.</summary>
    private readonly IRequestQueue<QueuedLookupRequest> _queue;
    /// <summary>Tracks per-provider, per-endpoint rate-limit windows.</summary>
    private readonly IRateLimitTracker _rateLimitTracker;
    /// <summary>Reads and updates the durable saga state shared with the orchestrator.</summary>
    private readonly ISagaStateManager _sagaManager;
    /// <summary>The provider lookup service that performs the actual API call for this provider.</summary>
    private readonly IMusicLookupService _lookupService;
    /// <summary>The provider this processor instance serves.</summary>
    private readonly SupportedProviders _provider;
    /// <summary>The logger for processing lifecycle, rate-limit, and failure diagnostics.</summary>
    private readonly ILogger<QueueProcessorBackgroundService> _logger;
    /// <summary>camelCase JSON options used to serialize provider results into saga state.</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Redis pub/sub channel announcing that a whole saga has completed.</summary>
    private const string SagaCompletedChannel = "saga:completed";
    /// <summary>Prefix for the per-lookup Redis channel (<c>complete:{lookupKey}</c>) that wakes orchestrator waiters.</summary>
    private const string LookupCompleteChannelPrefix = "complete:";
    /// <summary>Idle-poll delay applied when the queue has no message ready (100 ms).</summary>
    private static readonly TimeSpan s_noMessageDelay = TimeSpan.FromMilliseconds( 100 );
    /// <summary>Backoff applied after an unexpected loop error before retrying (1 second).</summary>
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromSeconds( 1 );

    /// <summary>
    /// Initializes a processor for a single provider with its queue, rate-limit tracker, saga manager,
    /// lookup service, Redis connection, and logger.
    /// </summary>
    /// <param name="redis">The Redis connection used to publish completion events.</param>
    /// <param name="queue">This provider's request queue.</param>
    /// <param name="rateLimitTracker">Tracker for per-provider, per-endpoint rate-limit windows.</param>
    /// <param name="sagaManager">Manager for the shared, durable saga state.</param>
    /// <param name="lookupService">The provider lookup service that performs the actual API call.</param>
    /// <param name="provider">The provider this processor serves.</param>
    /// <param name="logger">The logger for processing diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
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

    /// <summary>
    /// Runs the consume loop until the host stops. Ensures Redis Streams consumer groups exist, then
    /// repeatedly dequeues (rate-limit aware), processing each message or idle-polling when none is
    /// ready. Cancellation ends the loop; other loop-level errors are logged and followed by a short
    /// backoff before continuing.
    /// </summary>
    /// <param name="stoppingToken">Signals that the host is shutting down.</param>
    /// <returns>A task that completes when the processor stops.</returns>
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

    /// <summary>
    /// Processes one dequeued message end to end: rebuilds the lookup key, ensures the saga and this
    /// provider's state, pre-checks the endpoint rate limit (requeuing if limited), performs the
    /// lookup, records the result in saga state, publishes saga and lookup completion, and
    /// acknowledges. Rate-limit and other failures are routed to their handlers; processing duration
    /// is always recorded as a metric.
    /// </summary>
    /// <param name="message">The dequeued message carrying the lookup request.</param>
    /// <param name="ct">Cancels processing.</param>
    /// <returns>A task that completes when the message has been processed.</returns>
    private async Task ProcessMessageAsync( QueuedMessage<QueuedLookupRequest> message, CancellationToken ct ) {
        QueuedLookupRequest request = message.Payload;
        Stopwatch stopwatch = Stopwatch.StartNew( );
        string status = "success";

        LogProcessingMessage( _logger, message.MessageId, request.SagaId, request.LookupType );

        // Ensure saga exists before processing. This handles JetStream bulk lookups where
        // the saga ID is set but the saga hasn't been created yet (fire-and-forget pattern).
        // For web lookups, this will return the existing saga.
        // The lookupKey MUST use the same canonical format as LookupOrchestrator so that
        // orchestrator-produced and worker-produced saga IDs agree for the same entity.
        // Use LookupKeyBuilder to derive the key from the request's own fields.
        string lookupKey = request.LookupType is LookupRequestType.SongIdLookup or LookupRequestType.AlbumIdLookup
            ? LookupKeyBuilder.TypedKey( request.LookupType, _provider, request.LookupValue )
            : request.LookupType == LookupRequestType.UriLookup
                ? LookupKeyBuilder.UrlKey( request.LookupValue )
                : $"{request.LookupType}:{request.LookupValue}";
        _ = await _sagaManager.GetOrCreateAsync(
            request.SagaId,
            lookupKey,
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

    /// <summary>
    /// Dispatches a queued request to the matching method on the provider lookup service based on its
    /// <see cref="LookupRequestType"/> (URL, ISRC, UPC, song/album id, or title+artist search).
    /// </summary>
    /// <param name="request">The lookup request to perform.</param>
    /// <param name="ct">Cancels the lookup; checked before dispatch.</param>
    /// <returns>The provider result, or <see langword="null"/> when the provider found no match.</returns>
    /// <remarks>
    /// <see cref="LookupRequestType.ArtistAlbumLookup"/> and <see cref="LookupRequestType.AlbumTrackLookup"/>
    /// intentionally throw <see cref="NotImplementedException"/>: they are intra-provider sub-steps, not
    /// top-level queueable requests, so they are not supported as a dispatch target here. Any other
    /// unrecognized type, or a search type missing its title/artist, throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <exception cref="NotImplementedException">
    /// Thrown for <see cref="LookupRequestType.ArtistAlbumLookup"/> and
    /// <see cref="LookupRequestType.AlbumTrackLookup"/>, which are not supported as standalone requests.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown for an unsupported lookup type or missing required parameters.</exception>
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

    /// <summary>
    /// Handles a provider rate-limit hit: records the endpoint's retry window, marks the saga partial,
    /// merges this provider's rate-limit info into the saga (replacing any prior entry for it),
    /// publishes the rate-limited sentinel to wake waiters with a partial, then acknowledges the
    /// current message and re-enqueues the request at <see cref="QueuePriority.Background"/> with an
    /// incremented attempt count. An interactive-origin request that hits a limit is explicitly logged
    /// and metered as deferred to the background lane.
    /// </summary>
    /// <param name="message">The message being processed when the limit was hit.</param>
    /// <param name="request">The lookup request that hit the rate limit.</param>
    /// <param name="ex">The rate-limit exception carrying the retry-after window.</param>
    /// <param name="ct">Cancels the saga and queue operations.</param>
    /// <returns>A task that completes once the saga is updated and the request re-enqueued.</returns>
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

        if (request.OriginPriority == QueuePriority.Interactive) {
            LogInteractiveDeferredToBackground( _logger, request.SagaId, request.LookupType, endpoint, retryAfter );
            QueueMetrics.RecordInteractiveDeferral( _provider, endpoint );
        }

        await _queue.AcknowledgeAsync( message.MessageId, ct );
        await _queue.EnqueueAsync( requeuedRequest, QueuePriority.Background, ct );

        LogSagaMarkedPartial( _logger, request.SagaId, endpoint, _provider, retryAfter );
    }

    /// <summary>
    /// Handles a non-rate-limit processing failure. When the request has reached
    /// <see cref="LookupConstants.MaxQueueRetryAttempts"/>, marks this provider's saga state failed,
    /// moves the message to the dead-letter queue, and checks/publishes saga completion. Otherwise
    /// records the provider failure, checks/publishes saga completion, and acknowledges the message so
    /// it can be retried.
    /// </summary>
    /// <param name="message">The message being processed when the failure occurred.</param>
    /// <param name="request">The lookup request that failed.</param>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <param name="ct">Cancels the saga and queue operations.</param>
    /// <returns>A task that completes once the failure has been recorded and routed.</returns>
    private async Task HandleProcessingExceptionAsync(
        QueuedMessage<QueuedLookupRequest> message,
        QueuedLookupRequest request,
        Exception ex,
        CancellationToken ct
    ) {
        LogProcessingFailed( _logger, ex, request.RequestId, request.SagaId );

        // Check if we've exceeded retry attempts
        if (request.AttemptCount >= LookupConstants.MaxQueueRetryAttempts) {
            LogMaxRetriesExceeded( _logger, message.MessageId, LookupConstants.MaxQueueRetryAttempts );

            // Update saga with error state
            await _sagaManager.UpdateProviderStateAsync(
                request.SagaId,
                new ProviderLookupState(
                    Provider: _provider,
                    IsComplete: true,
                    IsSuccess: false,
                    ResultJson: null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: $"Failed after {LookupConstants.MaxQueueRetryAttempts} attempts: {ex.Message}"
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

    /// <summary>
    /// Publishes the saga-completed event on the <c>saga:completed</c> channel when the saga exists, is
    /// complete, and has not already been finalized. No-ops (with a diagnostic log) when the saga is
    /// missing, incomplete, or already has a final result. Failures are logged and swallowed.
    /// </summary>
    /// <param name="sagaId">The saga to check and possibly announce.</param>
    /// <param name="ct">Cancels the saga read.</param>
    /// <returns>A task that completes once the check (and any publish) finishes.</returns>
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

    /// <summary>
    /// Publishes the per-lookup completion on the <c>complete:{lookupKey}</c> channel, carrying the
    /// saga's partial-or-final result URI (or an empty string), to wake orchestrator waiters. No-ops
    /// (with a diagnostic log) when the saga is missing. Failures are logged and swallowed.
    /// </summary>
    /// <param name="sagaId">The saga whose lookup completion is announced.</param>
    /// <param name="ct">Cancels the saga read.</param>
    /// <returns>A task that completes once the publish (or no-op) finishes.</returns>
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

    /// <summary>
    /// Publishes the rate-limited sentinel on the <c>complete:{lookupKey}</c> channel, signaling
    /// waiters that pending providers are rate-limited and a partial result should be returned now.
    /// No-ops (with a diagnostic log) when the saga is missing. Failures are logged and swallowed.
    /// </summary>
    /// <param name="sagaId">The saga whose pending providers are rate-limited.</param>
    /// <param name="ct">Cancels the saga read.</param>
    /// <returns>A task that completes once the publish (or no-op) finishes.</returns>
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

    /// <summary>Logs (Information) that the queue processor is starting for a provider.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The provider this processor serves.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.QueueProcessorStarting,
        Level = LogLevel.Information,
        Message = "Queue processor starting for provider {Provider}" )]
    private static partial void LogQueueProcessorStarting( ILogger logger, SupportedProviders provider );

    /// <summary>Logs (Information) that the queue processor is stopping for a provider.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The provider this processor serves.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.QueueProcessorStopping,
        Level = LogLevel.Information,
        Message = "Queue processor stopping for provider {Provider}" )]
    private static partial void LogQueueProcessorStopping( ILogger logger, SupportedProviders provider );

    /// <summary>Logs (Error) that the consume loop threw; the loop backs off and continues.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception raised in the loop.</param>
    /// <param name="provider">The provider this processor serves.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.QueueProcessorLoopError,
        Level = LogLevel.Error,
        Message = "Error in queue processing loop for {Provider}" )]
    private static partial void LogQueueProcessorLoopError( ILogger logger, Exception ex, SupportedProviders provider );

    /// <summary>Logs (Debug) that a message is being processed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The queue message id.</param>
    /// <param name="sagaId">The saga the message belongs to.</param>
    /// <param name="lookupType">The lookup type being processed.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.ProcessingMessage,
        Level = LogLevel.Debug,
        Message = "Processing message {MessageId} for saga {SagaId}, lookup type {LookupType}" )]
    private static partial void LogProcessingMessage( ILogger logger, string messageId, string sagaId, LookupRequestType lookupType );

    /// <summary>Logs (Debug) that an endpoint is rate-limited and the message is being requeued.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="endpoint">The rate-limited endpoint.</param>
    /// <param name="retryAfter">The instant after which the endpoint may be retried.</param>
    /// <param name="messageId">The queue message id being requeued.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.EndpointRateLimited,
        Level = LogLevel.Debug,
        Message = "Endpoint {Endpoint} is rate-limited until {RetryAfter}, requeuing message {MessageId}" )]
    private static partial void LogEndpointRateLimited( ILogger logger, string endpoint, DateTimeOffset retryAfter, string messageId );

    /// <summary>Logs (Debug) that a message was processed successfully.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The queue message id.</param>
    /// <param name="sagaId">The saga the message belonged to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.MessageProcessed,
        Level = LogLevel.Debug,
        Message = "Successfully processed message {MessageId} for saga {SagaId}" )]
    private static partial void LogMessageProcessed( ILogger logger, string messageId, string sagaId );

    /// <summary>Logs (Warning) that the provider returned a rate-limit response for an endpoint.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The rate-limited provider.</param>
    /// <param name="endpoint">The rate-limited endpoint.</param>
    /// <param name="retryAfter">The retry-after window reported by the provider.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.RateLimitEncountered,
        Level = LogLevel.Warning,
        Message = "Rate limit encountered for {Provider} endpoint {Endpoint}, retry after {RetryAfter}" )]
    private static partial void LogRateLimitEncountered( ILogger logger, SupportedProviders provider, string endpoint, TimeSpan retryAfter );

    /// <summary>Logs (Information) that a saga was marked partial and the request requeued after a rate limit.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga marked partial.</param>
    /// <param name="endpoint">The rate-limited endpoint.</param>
    /// <param name="provider">The provider the request was requeued for.</param>
    /// <param name="retryAfter">The instant until which the request is deferred.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaMarkedPartial,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} marked as partial due to rate limit on {Endpoint}, requeued for {Provider} (will be deferred until {RetryAfter})" )]
    private static partial void LogSagaMarkedPartial( ILogger logger, string sagaId, string endpoint, SupportedProviders provider, DateTimeOffset retryAfter );

    /// <summary>Logs (Warning) that an interactive lookup was deferred to the background lane after a rate limit.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga whose interactive request was deferred.</param>
    /// <param name="lookupType">The lookup type that was deferred.</param>
    /// <param name="endpoint">The rate-limited endpoint.</param>
    /// <param name="retryAfter">The instant after which the request may be retried.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.InteractiveDeferredToBackground,
        Level = LogLevel.Warning,
        Message = "Interactive lookup {SagaId}/{LookupType} deferred to the background lane due to a rate limit on {Endpoint}, retry after {RetryAfter}" )]
    private static partial void LogInteractiveDeferredToBackground(
        ILogger logger,
        string sagaId,
        LookupRequestType lookupType,
        string endpoint,
        DateTimeOffset retryAfter );

    /// <summary>Logs (Error) that a lookup request failed to process.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <param name="requestId">The failing request id.</param>
    /// <param name="sagaId">The saga the request belonged to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.ProcessingFailed,
        Level = LogLevel.Error,
        Message = "Failed to process lookup request {RequestId} for saga {SagaId}" )]
    private static partial void LogProcessingFailed( ILogger logger, Exception ex, string requestId, string sagaId );

    /// <summary>Logs (Warning) that a message exceeded its retry budget and is being dead-lettered.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The queue message id moving to the DLQ.</param>
    /// <param name="maxRetries">The maximum retry attempts that were exceeded.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.MaxRetriesExceeded,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} exceeded max retry attempts ({MaxRetries}), moving to DLQ" )]
    private static partial void LogMaxRetriesExceeded( ILogger logger, string messageId, int maxRetries );

    /// <summary>Logs (Debug) that a saga could not be found when checking completion.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The missing saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaNotFoundForCompletion,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} not found, cannot check completion" )]
    private static partial void LogSagaNotFoundForCompletion( ILogger logger, string sagaId );

    /// <summary>Logs (Debug) that a saga is not yet complete, so no completion event is published.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaNotYetComplete,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} is not yet complete" )]
    private static partial void LogSagaNotYetComplete( ILogger logger, string sagaId );

    /// <summary>Logs (Debug) that a saga already has a final result, so completion is not re-published.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaAlreadyHasFinalResult,
        Level = LogLevel.Debug,
        Message = "Saga {SagaId} already has final result" )]
    private static partial void LogSagaAlreadyHasFinalResult( ILogger logger, string sagaId );

    /// <summary>Logs (Information) that a saga is complete and its completion event is being published.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The completed saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaCompletePublishing,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} is complete, publishing completion event" )]
    private static partial void LogSagaCompletePublishing( ILogger logger, string sagaId );

    /// <summary>Logs (Error) that checking or publishing saga completion failed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception raised.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaCompletionCheckFailed,
        Level = LogLevel.Error,
        Message = "Failed to check/publish saga completion for {SagaId}" )]
    private static partial void LogSagaCompletionCheckFailed( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs (Warning) that a saga could not be found when publishing lookup completion.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The missing saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.SagaNotFoundForLookupCompletion,
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} not found, cannot publish lookup completion" )]
    private static partial void LogSagaNotFoundForLookupCompletion( ILogger logger, string sagaId );

    /// <summary>Logs (Debug) that a lookup completion was published on a channel.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga whose completion was published.</param>
    /// <param name="channel">The channel the completion was published on.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.LookupCompletionPublished,
        Level = LogLevel.Debug,
        Message = "Published lookup completion for saga {SagaId} on channel {Channel}" )]
    private static partial void LogLookupCompletionPublished( ILogger logger, string sagaId, string channel );

    /// <summary>Logs (Error) that publishing a lookup completion failed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception raised.</param>
    /// <param name="sagaId">The saga id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Queue.LookupCompletionPublishFailed,
        Level = LogLevel.Error,
        Message = "Failed to publish lookup completion for {SagaId}" )]
    private static partial void LogLookupCompletionPublishFailed( ILogger logger, Exception ex, string sagaId );

    #endregion LoggerMessage Definitions
}
