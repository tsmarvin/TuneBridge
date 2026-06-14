using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Worker.Spotify.Logging;
using BridgeBeats.Worker.Spotify.Metrics;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Background service that batches Spotify single-id track and album lookups and resolves them via
/// Spotify's multi-id bulk API.
/// </summary>
/// <remarks>
/// Spotify's API allows fetching many ids in one call, so single-id lookups are diverted onto
/// dedicated bulk streams (drained through <see cref="SpotifyBatchQueueHelper"/>) and processed here
/// in batches. The loop polls every 500ms and flushes a stream when its depth reaches the per-batch
/// maximum or when its oldest queued item exceeds the configured linger window
/// (<see cref="ShouldFlush"/>). On success it drives the same saga update and completion-publish path
/// as the per-message queue processor. Failure handling has three distinct shapes: a rate-limit
/// (<see cref="RetryAfterExceededException"/>) marks affected sagas partial and requeues the whole
/// batch; an empty result dictionary is treated as a request-level failure that arms an exponential
/// per-stream cooldown and requeues the batch; and an absent key within a non-empty result is a
/// per-id partial that requeues only that message.
/// </remarks>
public sealed partial class SpotifyBulkProcessorService : BackgroundService {

    /// <summary>Redis connection multiplexer used to publish completion and sentinel events.</summary>
    private readonly IConnectionMultiplexer _redis;

    /// <summary>Helper that owns the bulk-stream consumer groups, dequeue, ack, and requeue.</summary>
    private readonly SpotifyBatchQueueHelper _batchHelper;

    /// <summary>Tracker used to read and set per-endpoint rate-limit state.</summary>
    private readonly IRateLimitTracker _rateLimitTracker;

    /// <summary>Manager used to create, update, and inspect saga state.</summary>
    private readonly ISagaStateManager _sagaManager;

    /// <summary>Spotify bulk lookup service that performs the multi-id API calls.</summary>
    private readonly ISpotifyBulkLookupService _lookupService;

    /// <summary>Logger for this service's structured log events.</summary>
    private readonly ILogger<SpotifyBulkProcessorService> _logger;

    /// <summary>JSON options used to serialize provider results into saga state (camelCase, compact).</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>The batch linger window; a batch under the size threshold flushes once its oldest item reaches this age.</summary>
    private readonly TimeSpan _batchLinger;

    /// <summary>Base request-failure cooldown in seconds used as the exponent base for backoff.</summary>
    private readonly int _requestFailureCooldownSeconds;

    // Request-failure cooldown state — suppresses flush after empty-dict (network/auth error)
    // to avoid burning retry attempts faster than Spotify can recover.
    // Cooldown doubles per consecutive failure, capped at SpotifyBatchSettings.MaxRequestFailureCooldownSeconds.
    // Each stream maintains its own independent cooldown: a tracks-endpoint failure does not
    // suppress album flushes (and vice versa), consistent with per-endpoint rate-limit isolation.

    /// <summary>Time until which the track-id stream is in request-failure cooldown.</summary>
    private DateTimeOffset _trackIdCooldownUntil = DateTimeOffset.MinValue;

    /// <summary>Count of consecutive track-id request failures, driving exponential cooldown growth.</summary>
    private int _consecutiveTrackIdFailures;

    /// <summary>Time until which the album-id stream is in request-failure cooldown.</summary>
    private DateTimeOffset _albumIdCooldownUntil = DateTimeOffset.MinValue;

    /// <summary>Count of consecutive album-id request failures, driving exponential cooldown growth.</summary>
    private int _consecutiveAlbumIdFailures;

    /// <summary>Pub/Sub channel on which saga-level completion is published.</summary>
    private const string SagaCompletedChannel = "saga:completed";

    /// <summary>Prefix of the per-lookup Pub/Sub completion channel (<c>complete:{lookupKey}</c>).</summary>
    private const string LookupCompleteChannelPrefix = "complete:";

    /// <summary>Interval between bulk-flush checks (500ms).</summary>
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromMilliseconds( 500 );

    /// <summary>Delay applied after an unhandled loop error before retrying (5 seconds).</summary>
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromSeconds( 5 );

    /// <summary>
    /// Initializes the service from its dependencies and binds the batch linger and failure-cooldown
    /// settings.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer for publishing completion events.</param>
    /// <param name="batchHelper">Helper for bulk-stream dequeue, ack, and requeue.</param>
    /// <param name="rateLimitTracker">Tracker for per-endpoint rate-limit state.</param>
    /// <param name="sagaManager">Manager for saga state.</param>
    /// <param name="lookupService">Spotify bulk lookup service.</param>
    /// <param name="logger">Logger for the service.</param>
    /// <param name="batchSettings">Bound batch settings supplying the linger window and base cooldown.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency or <paramref name="batchSettings"/> is <see langword="null"/>.</exception>
    public SpotifyBulkProcessorService(
        IConnectionMultiplexer redis,
        SpotifyBatchQueueHelper batchHelper,
        IRateLimitTracker rateLimitTracker,
        ISagaStateManager sagaManager,
        ISpotifyBulkLookupService lookupService,
        ILogger<SpotifyBulkProcessorService> logger,
        IOptions<SpotifyBatchSettings> batchSettings
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _batchHelper = batchHelper ?? throw new ArgumentNullException( nameof( batchHelper ) );
        _rateLimitTracker = rateLimitTracker ?? throw new ArgumentNullException( nameof( rateLimitTracker ) );
        _sagaManager = sagaManager ?? throw new ArgumentNullException( nameof( sagaManager ) );
        _lookupService = lookupService ?? throw new ArgumentNullException( nameof( lookupService ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        ArgumentNullException.ThrowIfNull( batchSettings );
        int lingerMs = batchSettings.Value.LingerMs;
        _batchLinger = TimeSpan.FromMilliseconds( lingerMs );
        _requestFailureCooldownSeconds = batchSettings.Value.RequestFailureCooldownSeconds;

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Ensures the bulk-stream consumer groups exist, then loops every check interval flushing the
    /// track-id and album-id streams whenever their flush conditions are met, until cancellation.
    /// </summary>
    /// <param name="stoppingToken">Token signaled when the host is shutting down.</param>
    /// <returns>A task that completes when the loop exits.</returns>
    /// <remarks>Unhandled errors in an iteration are logged and retried after a short delay rather than crashing the host.</remarks>
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

    // Pure flush predicate — extracted for testability

    /// <summary>
    /// Decides whether a bulk batch should be flushed now, based on its size and the age of its
    /// oldest queued item (the batch linger window).
    /// </summary>
    /// <param name="count">The current number of queued items.</param>
    /// <param name="oldestAge">The age of the oldest queued item, or <see langword="null"/> when unknown.</param>
    /// <param name="threshold">The per-batch size at which a flush is forced.</param>
    /// <param name="linger">The maximum age the oldest item may reach before a flush is forced.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="count"/> reaches <paramref name="threshold"/>, or
    /// when there is at least one item whose age has reached <paramref name="linger"/>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool ShouldFlush( int count, TimeSpan? oldestAge, int threshold, TimeSpan linger ) {
        if (count >= threshold) {
            return true;
        }
        return count > 0 && oldestAge.HasValue && oldestAge.Value >= linger;
    }

    // Flush-decision methods

    /// <summary>
    /// Determines whether the bulk track-id stream should be flushed this cycle.
    /// </summary>
    /// <param name="ct">Token used to cancel the checks.</param>
    /// <returns>
    /// <see langword="false"/> when the bulk-tracks endpoint is rate-limited, the stream is in
    /// request-failure cooldown, or the stream is empty; otherwise the result of
    /// <see cref="ShouldFlush"/> for the current depth and oldest-item age.
    /// </returns>
    private async Task<bool> ShouldProcessBulkTracksAsync( CancellationToken ct ) {
        // Rate-limit guard: don't call the bulk endpoint if it's currently limited
        RateLimitState rateLimitState = await _rateLimitTracker.GetStateAsync(
            SupportedProviders.Spotify,
            SpotifyConstants.BulkTracksEndpoint,
            ct
        );

        if (rateLimitState.IsRateLimited) {
            if (_logger.IsEnabled( LogLevel.Warning )) {
                string retryAfterStr = rateLimitState.RetryAfter?.ToString( ) ?? "unknown";
                LogRateLimited( _logger, "Bulk tracks", retryAfterStr );
            }
            return false;
        }

        // Request-failure cooldown guard: suppress flush while in backoff after an
        // empty-dict failure so all attempts are not burned during a transient Spotify outage.
        if (DateTimeOffset.UtcNow < _trackIdCooldownUntil) {
            return false;
        }

        IdLookupDepth depth = await _batchHelper.GetIdLookupDepthAsync( ct );
        if (depth.BulkTrackIdCount == 0) {
            return false;
        }

        TimeSpan? oldestAge = null;
        if (depth.BulkTrackIdCount < SpotifyConstants.MaxTracksPerBatchLookup) {
            DateTimeOffset? oldest = await _batchHelper.GetOldestEnqueuedAtAsync( isTracks: true, ct );
            if (oldest.HasValue) {
                oldestAge = DateTimeOffset.UtcNow - oldest.Value;
            }
        }

        return ShouldFlush( depth.BulkTrackIdCount, oldestAge, SpotifyConstants.MaxTracksPerBatchLookup, _batchLinger );
    }

    /// <summary>
    /// Determines whether the bulk album-id stream should be flushed this cycle.
    /// </summary>
    /// <param name="ct">Token used to cancel the checks.</param>
    /// <returns>
    /// <see langword="false"/> when the bulk-albums endpoint is rate-limited, the stream is in
    /// request-failure cooldown, or the stream is empty; otherwise the result of
    /// <see cref="ShouldFlush"/> for the current depth and oldest-item age.
    /// </returns>
    private async Task<bool> ShouldProcessBulkAlbumsAsync( CancellationToken ct ) {
        // Rate-limit guard: don't call the bulk endpoint if it's currently limited
        RateLimitState rateLimitState = await _rateLimitTracker.GetStateAsync(
            SupportedProviders.Spotify,
            SpotifyConstants.BulkAlbumsEndpoint,
            ct
        );

        if (rateLimitState.IsRateLimited) {
            if (_logger.IsEnabled( LogLevel.Warning )) {
                string retryAfterStr = rateLimitState.RetryAfter?.ToString( ) ?? "unknown";
                LogRateLimited( _logger, "Bulk albums", retryAfterStr );
            }
            return false;
        }

        // Request-failure cooldown guard
        if (DateTimeOffset.UtcNow < _albumIdCooldownUntil) {
            return false;
        }

        IdLookupDepth depth = await _batchHelper.GetIdLookupDepthAsync( ct );
        if (depth.BulkAlbumIdCount == 0) {
            return false;
        }

        TimeSpan? oldestAge = null;
        if (depth.BulkAlbumIdCount < SpotifyConstants.MaxAlbumsPerBatchLookup) {
            DateTimeOffset? oldest = await _batchHelper.GetOldestEnqueuedAtAsync( isTracks: false, ct );
            if (oldest.HasValue) {
                oldestAge = DateTimeOffset.UtcNow - oldest.Value;
            }
        }

        return ShouldFlush( depth.BulkAlbumIdCount, oldestAge, SpotifyConstants.MaxAlbumsPerBatchLookup, _batchLinger );
    }

    // Batch-processing methods

    /// <summary>
    /// Dequeues and resolves a batch of track-id lookups via the bulk Spotify API, then applies each
    /// result to its saga.
    /// </summary>
    /// <param name="ct">Token used to stop processing early.</param>
    /// <returns>A task that completes once the batch has been resolved, applied, or requeued.</returns>
    /// <remarks>
    /// Messages are grouped by track id so duplicates share one API result. An empty result
    /// dictionary is treated as a request-level failure: it arms the exponential track-id cooldown
    /// and requeues the whole batch. A present-but-<see langword="null"/> result records a
    /// not-found provider state; an absent key requeues only that message. A
    /// <see cref="RetryAfterExceededException"/> is routed to <see cref="HandleBulkRateLimitAsync"/>,
    /// and any other exception requeues the whole batch.
    /// </remarks>
    internal async Task ProcessBulkTrackLookupsAsync( CancellationToken ct ) {
        LogProcessingBulkTracks( _logger );

        // Dequeue directly from the type-specific stream only.
        // Top-up from generic priority streams is intentionally omitted (would race the generic worker's unacked deliveries).
        List<QueuedMessage<QueuedLookupRequest>> messages =
            [.. await _batchHelper.DequeueTrackIdBatchAsync( SpotifyConstants.MaxTracksPerBatchLookup, ct )];

        if (messages.Count == 0) {
            LogNoTrackLookups( _logger );
            return;
        }

        LogProcessingTrackCount( _logger, messages.Count );
        SpotifyBatchMetrics.RecordBatchSize( "tracks", messages.Count );

        // Build a track-ID → messages map so each bulk API result maps back to its request(s)
        Dictionary<string, List<QueuedMessage<QueuedLookupRequest>>> idToMessages = [];
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            string trackId = message.Payload.LookupValue;
            if (!idToMessages.TryGetValue( trackId, out List<QueuedMessage<QueuedLookupRequest>>? value )) {
                value = [];
                idToMessages[trackId] = value;
            }
            value.Add( message );
        }

        try {
            // Call the bulk lookup API (GET /tracks?ids=...)
            Dictionary<string, MusicLookupResult?> results = await _lookupService.GetTracksByIdsAsync( idToMessages.Keys );

            // Empty dictionary = request failure (auth error, network error).
            // Requeue ALL messages without writing any saga state. Arm the request-failure
            // cooldown so the linger-trigger does not immediately re-flush.
            if (results.Count == 0) {
                LogBulkDispatchRequeuingAll( _logger, messages.Count, "empty-dict (request failure)" );
                ArmRequestFailureCooldown( ref _trackIdCooldownUntil, ref _consecutiveTrackIdFailures );
                await RequeueAllAsync( messages, ct );
                return;
            }

            // Success: reset the consecutive-failure counter for this stream.
            _consecutiveTrackIdFailures = 0;
            _trackIdCooldownUntil = DateTimeOffset.MinValue;

            // Dispatch each result to its queued messages
            foreach (KeyValuePair<string, List<QueuedMessage<QueuedLookupRequest>>> kvp in idToMessages) {
                string trackId = kvp.Key;
                bool keyPresent = results.TryGetValue( trackId, out MusicLookupResult? result );

                foreach (QueuedMessage<QueuedLookupRequest> message in kvp.Value) {
                    if (!keyPresent) {
                        // Key absent from non-empty dict = partial parse failure.
                        // Requeue this individual message, no saga write.
                        LogBulkDispatchRequeuingOne( _logger, trackId, "absent key (partial parse)" );
                        await RequeueSingleAsync( message, ct );
                    } else {
                        // key present, result may be null (genuine not-found) — correct path
                        await ProcessBulkResultAsync( message, result, ct );
                    }
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
    /// Dequeues and resolves a batch of album-id lookups via the bulk Spotify API, then applies each
    /// result to its saga.
    /// </summary>
    /// <param name="ct">Token used to stop processing early.</param>
    /// <returns>A task that completes once the batch has been resolved, applied, or requeued.</returns>
    /// <remarks>
    /// Mirrors <see cref="ProcessBulkTrackLookupsAsync"/> for albums: messages are grouped by album
    /// id, an empty result dictionary arms the album-id cooldown and requeues the batch, an absent
    /// key requeues only that message, a rate limit routes to <see cref="HandleBulkRateLimitAsync"/>,
    /// and any other exception requeues the whole batch.
    /// </remarks>
    internal async Task ProcessBulkAlbumLookupsAsync( CancellationToken ct ) {
        LogProcessingBulkAlbums( _logger );

        // Type-specific stream only; no priority-stream top-up.
        List<QueuedMessage<QueuedLookupRequest>> messages =
            [.. await _batchHelper.DequeueAlbumIdBatchAsync( SpotifyConstants.MaxAlbumsPerBatchLookup, ct )];

        if (messages.Count == 0) {
            LogNoAlbumLookups( _logger );
            return;
        }

        LogProcessingAlbumCount( _logger, messages.Count );
        SpotifyBatchMetrics.RecordBatchSize( "albums", messages.Count );

        // Build an album-ID → messages map
        Dictionary<string, List<QueuedMessage<QueuedLookupRequest>>> idToMessages = [];
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            string albumId = message.Payload.LookupValue;
            if (!idToMessages.TryGetValue( albumId, out List<QueuedMessage<QueuedLookupRequest>>? value )) {
                value = [];
                idToMessages[albumId] = value;
            }
            value.Add( message );
        }

        try {
            // Call the bulk lookup API (GET /albums?ids=...)
            Dictionary<string, MusicLookupResult?> results = await _lookupService.GetAlbumsByIdsAsync( idToMessages.Keys );

            // Empty dictionary = request failure → requeue ALL. Arm cooldown.
            if (results.Count == 0) {
                LogBulkDispatchRequeuingAll( _logger, messages.Count, "empty-dict (request failure)" );
                ArmRequestFailureCooldown( ref _albumIdCooldownUntil, ref _consecutiveAlbumIdFailures );
                await RequeueAllAsync( messages, ct );
                return;
            }

            // Success: reset cooldown counter.
            _consecutiveAlbumIdFailures = 0;
            _albumIdCooldownUntil = DateTimeOffset.MinValue;

            // Dispatch each result to its queued messages
            foreach (KeyValuePair<string, List<QueuedMessage<QueuedLookupRequest>>> kvp in idToMessages) {
                string albumId = kvp.Key;
                bool keyPresent = results.TryGetValue( albumId, out MusicLookupResult? result );

                foreach (QueuedMessage<QueuedLookupRequest> message in kvp.Value) {
                    if (!keyPresent) {
                        // Absent key in non-empty dict = partial parse failure → requeue one
                        LogBulkDispatchRequeuingOne( _logger, albumId, "absent key (partial parse)" );
                        await RequeueSingleAsync( message, ct );
                    } else {
                        await ProcessBulkResultAsync( message, result, ct );
                    }
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
    /// Applies a single resolved bulk result to its saga, then publishes completion and acknowledges
    /// the message.
    /// </summary>
    /// <param name="message">The queued message whose saga is being updated.</param>
    /// <param name="result">The resolved result, or <see langword="null"/> when the id was not found.</param>
    /// <param name="ct">Token used to cancel the saga and publish operations.</param>
    /// <returns>A task that completes once the saga is updated and the message acknowledged.</returns>
    /// <remarks>
    /// Ensures the saga exists, initializes the Spotify provider state, writes the provider result
    /// (success when <paramref name="result"/> is non-null, otherwise a "Not found" failure), then
    /// publishes saga-level and per-lookup completion before acknowledging. On error the message is
    /// requeued.
    /// </remarks>
    private async Task ProcessBulkResultAsync(
        QueuedMessage<QueuedLookupRequest> message,
        MusicLookupResult? result,
        CancellationToken ct
    ) {
        QueuedLookupRequest request = message.Payload;

        try {
            // Ensure the saga exists. JetStream fire-and-forget messages pre-compute a
            // SagaId and enqueue without creating the saga (the generic worker normally does
            // this). GetOrCreateAsync is idempotent — if the saga already exists it returns it.
            // Use LookupKeyBuilder so the lookupKey matches the orchestrator format for typed ID lookups.
            string lookupKey = LookupKeyBuilder.TypedKey( request.LookupType, SupportedProviders.Spotify, request.LookupValue );
            _ = await _sagaManager.GetOrCreateAsync(
                request.SagaId,
                lookupKey,
                request.LookupType,
                request.LookupValue,
                request.OriginPriority,
                ct
            );

            // Allocate the Spotify provider slot in the saga (idempotent)
            await _sagaManager.InitializeProviderStatesAsync( request.SagaId, [SupportedProviders.Spotify], ct );

            // Write the lookup result (found or not-found)
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

            // Check if saga is complete and publish completion events
            await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );
            await PublishLookupCompletionAsync( request.SagaId, ct );

            // Acknowledge the message
            await _batchHelper.AcknowledgeAsync( message.MessageId );

            LogBulkResultProcessed( _logger, request.SagaId, result is not null ? "found" : "not found" );
        } catch (Exception ex) {
            LogBulkResultError( _logger, ex, request.SagaId );
            await RequeueSingleAsync( message, ct );
        }
    }

    // Rate-limit and requeue helpers

    /// <summary>
    /// Handles a bulk-endpoint rate limit by recording it, marking each affected saga partial, and
    /// requeuing the whole batch.
    /// </summary>
    /// <param name="messages">The messages whose batch hit the rate limit.</param>
    /// <param name="endpoint">The bulk endpoint that was rate-limited.</param>
    /// <param name="ex">The exception carrying the retry-after value.</param>
    /// <param name="ct">Token used to cancel the work.</param>
    /// <returns>A task that completes once all sagas are marked and the batch requeued.</returns>
    /// <remarks>
    /// Sets the endpoint rate limit in the tracker, then for each message marks its saga partial,
    /// merges a <see cref="ProviderRateLimitInfo"/> for Spotify into the saga, and publishes a
    /// rate-limit sentinel so synchronous waiters receive a signal. The entire batch is requeued at
    /// the end. Per-saga errors are logged and do not stop the loop.
    /// </remarks>
    internal async Task HandleBulkRateLimitAsync(
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages,
        string endpoint,
        RetryAfterExceededException ex,
        CancellationToken ct
    ) {
        LogRateLimitEncountered( _logger, endpoint, ex.RetryAfterValue.ToString( ) );

        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.Add( ex.RetryAfterValue );
        await _rateLimitTracker.SetRateLimitedAsync( SupportedProviders.Spotify, endpoint, retryAfter, ct );

        // SetIsPartialAsync + merge SetRateLimitInfoAsync + publish rate-limited sentinel so
        // interactive callers whose request lands in a bulk batch do not hang until timeout on a 429.
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            if (ct.IsCancellationRequested) { break; }
            QueuedLookupRequest request = message.Payload;
            try {
                // Ensure the saga hash exists before writing provider state — JetStream fire-and-forget items pre-compute a SagaId without creating the saga.
                string lookupKey = LookupKeyBuilder.TypedKey( request.LookupType, SupportedProviders.Spotify, request.LookupValue );
                _ = await _sagaManager.GetOrCreateAsync( request.SagaId, lookupKey, request.LookupType, request.LookupValue, request.OriginPriority, ct );

                await _sagaManager.SetIsPartialAsync( request.SagaId, true, ct );

                LookupSagaState? saga = await _sagaManager.GetAsync( request.SagaId, ct );
                List<ProviderRateLimitInfo> mergedRateLimitInfo = saga?.RateLimitInfo is not null
                    ? [.. saga.RateLimitInfo.Where( r => r.Provider != SupportedProviders.Spotify )]
                    : [];
                mergedRateLimitInfo.Add( new ProviderRateLimitInfo( SupportedProviders.Spotify, retryAfter, endpoint ) );
                await _sagaManager.SetRateLimitInfoAsync( request.SagaId, mergedRateLimitInfo, ct );

                await PublishRateLimitSentinelAsync( request.SagaId, ct );

                LogBulkSagaMarkedPartial( _logger, request.SagaId, endpoint, retryAfter );
            } catch (Exception sagaEx) {
                LogBulkResultError( _logger, sagaEx, request.SagaId );
            }
        }

        // Requeue all messages (with AttemptCount increment via RequeueAsync)
        await RequeueAllAsync( messages, ct );
    }

    /// <summary>
    /// Requeues every message in a batch, continuing past per-message errors.
    /// </summary>
    /// <param name="messages">The messages to requeue.</param>
    /// <param name="ct">Token used to stop requeuing early.</param>
    /// <returns>A task that completes once all messages have been attempted.</returns>
    private async Task RequeueAllAsync(
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages,
        CancellationToken ct
    ) {
        foreach (QueuedMessage<QueuedLookupRequest> message in messages) {
            if (ct.IsCancellationRequested) { break; }
            try {
                await RequeueSingleAsync( message, ct );
            } catch (Exception ex) {
                LogRequeueError( _logger, ex, message.MessageId );
            }
        }
    }

    /// <summary>
    /// Requeues a single message and, when its retry cap is reached, finalizes the saga as a failure.
    /// </summary>
    /// <param name="message">The message to requeue.</param>
    /// <param name="ct">Token used to cancel the work.</param>
    /// <returns>A task that completes once the message is requeued or its saga is finalized.</returns>
    /// <remarks>
    /// When <see cref="SpotifyBatchQueueHelper.RequeueAsync"/> returns
    /// <see cref="RequeueOutcome.CapReached"/>, the Spotify provider state is written as a failure and
    /// saga-level and per-lookup completion are published so the lookup does not hang. Other outcomes
    /// require no further action here.
    /// </remarks>
    private async Task RequeueSingleAsync( QueuedMessage<QueuedLookupRequest> message, CancellationToken ct ) {
        QueuedLookupRequest request = message.Payload;
        RequeueOutcome outcome = await _batchHelper.RequeueAsync( message.MessageId, request.SagaId, ct );

        if (outcome == RequeueOutcome.CapReached) {
            // Cap hit or unrecoverable payload — write the failed provider state and
            // publish completion so the saga coordinator and interactive waiters unblock.
            try {
                // Ensure the saga hash exists before writing provider state — JetStream fire-and-forget items pre-compute a SagaId without creating the saga.
                string lookupKey = LookupKeyBuilder.TypedKey( request.LookupType, SupportedProviders.Spotify, request.LookupValue );
                _ = await _sagaManager.GetOrCreateAsync( request.SagaId, lookupKey, request.LookupType, request.LookupValue, request.OriginPriority, ct );

                await _sagaManager.UpdateProviderStateAsync(
                    request.SagaId,
                    new ProviderLookupState(
                        Provider: SupportedProviders.Spotify,
                        IsComplete: true,
                        IsSuccess: false,
                        ResultJson: null,
                        CompletedAt: DateTimeOffset.UtcNow,
                        ErrorMessage: $"Bulk lookup failed after {LookupConstants.MaxQueueRetryAttempts} attempts"
                    ),
                    ct
                );
                await CheckAndPublishSagaCompletionAsync( request.SagaId, ct );
                await PublishLookupCompletionAsync( request.SagaId, ct );
            } catch (Exception ex) {
                LogBulkResultError( _logger, ex, request.SagaId );
            }
        }
        // RequeueOutcome.NotFound: entry already XDELed — another consumer processed it.
        // Leave the saga untouched; logging was done inside RequeueAsync.
        // RequeueOutcome.Requeued: nothing to do here.
    }

    /// <summary>
    /// Increments a stream's consecutive-failure count and sets its cooldown deadline using
    /// exponential backoff.
    /// </summary>
    /// <param name="cooldownUntil">The stream's cooldown deadline, updated to now plus the computed cooldown.</param>
    /// <param name="consecutiveFailures">The stream's consecutive-failure counter, incremented in place.</param>
    /// <remarks>The cooldown grows with <see cref="ComputeCooldownSeconds"/> and is capped at <see cref="SpotifyBatchSettings.MaxRequestFailureCooldownSeconds"/>.</remarks>
    private void ArmRequestFailureCooldown( ref DateTimeOffset cooldownUntil, ref int consecutiveFailures ) {
        consecutiveFailures++;
        int cooldownSeconds = ComputeCooldownSeconds(
            consecutiveFailures,
            _requestFailureCooldownSeconds,
            SpotifyBatchSettings.MaxRequestFailureCooldownSeconds
        );
        cooldownUntil = DateTimeOffset.UtcNow.AddSeconds( cooldownSeconds );
    }

    /// <summary>
    /// Computes the request-failure cooldown in seconds using exponential backoff with a cap.
    /// The exponent is clamped to 5 before the left-shift to prevent integer overflow when
    /// <paramref name="consecutiveFailures"/> is large.
    /// </summary>
    /// <param name="consecutiveFailures">The number of consecutive failures (one-based; must be ≥ 1).</param>
    /// <param name="baseSeconds">The base cooldown applied to the first failure.</param>
    /// <param name="maxSeconds">The maximum cooldown the result is clamped to.</param>
    /// <returns>
    /// <paramref name="baseSeconds"/> doubled once per prior failure (the exponent is capped at five),
    /// clamped to <paramref name="maxSeconds"/>.
    /// </returns>
    internal static int ComputeCooldownSeconds( int consecutiveFailures, int baseSeconds, int maxSeconds ) {
        int exp = Math.Min( consecutiveFailures - 1, 5 );
        return Math.Min( baseSeconds * (1 << exp), maxSeconds );
    }

    // Saga completion and pub/sub helpers

    /// <summary>
    /// Publishes a saga-level completion event when the saga is complete and not yet finalized.
    /// </summary>
    /// <param name="sagaId">The saga to check and publish for.</param>
    /// <param name="ct">Token used to cancel the saga read.</param>
    /// <returns>A task that completes once the check (and any publish) finishes.</returns>
    /// <remarks>
    /// No event is published when the saga is missing, not yet complete, or already has a final result
    /// uri. Errors are logged and swallowed so a publish failure does not abort batch processing.
    /// </remarks>
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
    /// Publishes a per-lookup completion event so synchronous waiters on the lookup are woken.
    /// </summary>
    /// <param name="sagaId">The saga whose lookup completion is published.</param>
    /// <param name="ct">Token used to cancel the saga read.</param>
    /// <returns>A task that completes once the event is published.</returns>
    /// <remarks>
    /// Publishes the saga's partial or final result uri (or an empty string when neither is set) on
    /// the <c>complete:{lookupKey}</c> channel. Nothing is published when the saga is missing; errors
    /// are logged and swallowed.
    /// </remarks>
    private async Task PublishLookupCompletionAsync( string sagaId, CancellationToken ct ) {
        try {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );

            if (saga is null) {
                return;
            }

            string resultUri = saga.PartialResultUri ?? saga.FinalResultUri ?? string.Empty;
            string channel = $"{LookupCompleteChannelPrefix}{saga.LookupKey}";
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( channel ), resultUri );
        } catch (Exception ex) {
            LogPublishCompletionError( _logger, ex, sagaId );
        }
    }

    /// <summary>
    /// Publishes a rate-limit sentinel on the saga's per-lookup channel so waiters learn the lookup
    /// was deferred rather than completed.
    /// </summary>
    /// <param name="sagaId">The saga whose lookup is rate-limited.</param>
    /// <param name="ct">Token used to cancel the saga read.</param>
    /// <returns>A task that completes once the sentinel is published.</returns>
    /// <remarks>Nothing is published when the saga is missing; errors are logged and swallowed.</remarks>
    private async Task PublishRateLimitSentinelAsync( string sagaId, CancellationToken ct ) {
        try {
            LookupSagaState? saga = await _sagaManager.GetAsync( sagaId, ct );
            if (saga is null) { return; }

            string channel = $"{LookupCompleteChannelPrefix}{saga.LookupKey}";
            ISubscriber subscriber = _redis.GetSubscriber( );
            _ = await subscriber.PublishAsync( RedisChannel.Literal( channel ), LookupConstants.RateLimitedSentinel );
        } catch (Exception ex) {
            LogBulkPublishRateLimitSentinelError( _logger, ex, sagaId );
        }
    }

    #region LoggerMessage Methods

    /// <summary>Logs that the bulk processor service has started.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkProcessorStarting,
        Level = LogLevel.Information,
        Message = "Spotify bulk processor service starting" )]
    private static partial void LogServiceStarting( ILogger logger );

    /// <summary>Logs an unhandled error in the bulk processor loop.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkProcessorLoopError,
        Level = LogLevel.Error,
        Message = "Error in bulk processor loop" )]
    private static partial void LogProcessorLoopError( ILogger logger, Exception ex );

    /// <summary>Logs that the bulk processor service is stopping.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkProcessorStopping,
        Level = LogLevel.Information,
        Message = "Spotify bulk processor service stopping" )]
    private static partial void LogServiceStopping( ILogger logger );

    /// <summary>Logs that a bulk endpoint is rate-limited and its batch was deferred.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="endpoint">The rate-limited endpoint.</param>
    /// <param name="retryAfter">The time until which the endpoint is rate-limited.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkRateLimited,
        Level = LogLevel.Debug,
        Message = "{Endpoint} endpoint is rate-limited until {RetryAfter}" )]
    private static partial void LogRateLimited( ILogger logger, string endpoint, string retryAfter );

    /// <summary>Logs that a bulk track-id flush is starting.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingBulkTracks,
        Level = LogLevel.Information,
        Message = "Processing bulk track lookups" )]
    private static partial void LogProcessingBulkTracks( ILogger logger );

    /// <summary>Logs that no track-id lookups were available to process.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.NoTrackLookups,
        Level = LogLevel.Debug,
        Message = "No track ID lookups to process" )]
    private static partial void LogNoTrackLookups( ILogger logger );

    /// <summary>Logs the number of track-id lookups in the current batch.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of track-id lookups being processed.</param>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingTrackCount,
        Level = LogLevel.Information,
        Message = "Processing {Count} track ID lookups in bulk" )]
    private static partial void LogProcessingTrackCount( ILogger logger, int count );

    /// <summary>Logs that a batch of track-id lookups was processed successfully.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of track-id lookups processed.</param>
    [LoggerMessage(
        EventId = LogEventIds.TrackLookupsSuccess,
        Level = LogLevel.Information,
        Message = "Successfully processed {Count} track ID lookups" )]
    private static partial void LogTrackLookupsSuccess( ILogger logger, int count );

    /// <summary>Logs an error while processing bulk track-id lookups.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.TrackLookupsError,
        Level = LogLevel.Error,
        Message = "Error processing bulk track lookups" )]
    private static partial void LogTrackLookupsError( ILogger logger, Exception ex );

    /// <summary>Logs that a bulk album-id flush is starting.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingBulkAlbums,
        Level = LogLevel.Information,
        Message = "Processing bulk album lookups" )]
    private static partial void LogProcessingBulkAlbums( ILogger logger );

    /// <summary>Logs that no album-id lookups were available to process.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.NoAlbumLookups,
        Level = LogLevel.Debug,
        Message = "No album ID lookups to process" )]
    private static partial void LogNoAlbumLookups( ILogger logger );

    /// <summary>Logs the number of album-id lookups in the current batch.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of album-id lookups being processed.</param>
    [LoggerMessage(
        EventId = LogEventIds.ProcessingAlbumCount,
        Level = LogLevel.Information,
        Message = "Processing {Count} album ID lookups in bulk" )]
    private static partial void LogProcessingAlbumCount( ILogger logger, int count );

    /// <summary>Logs that a batch of album-id lookups was processed successfully.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of album-id lookups processed.</param>
    [LoggerMessage(
        EventId = LogEventIds.AlbumLookupsSuccess,
        Level = LogLevel.Information,
        Message = "Successfully processed {Count} album ID lookups" )]
    private static partial void LogAlbumLookupsSuccess( ILogger logger, int count );

    /// <summary>Logs an error while processing bulk album-id lookups.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.AlbumLookupsError,
        Level = LogLevel.Error,
        Message = "Error processing bulk album lookups" )]
    private static partial void LogAlbumLookupsError( ILogger logger, Exception ex );

    /// <summary>Logs that a single bulk result was applied to its saga.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga the result was applied to.</param>
    /// <param name="result">A short outcome label (for example <c>found</c> or <c>not found</c>).</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkResultProcessed,
        Level = LogLevel.Debug,
        Message = "Processed bulk result for saga {SagaId}: {Result}" )]
    private static partial void LogBulkResultProcessed( ILogger logger, string sagaId, string result );

    /// <summary>Logs an error while applying a single bulk result to its saga.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="sagaId">The saga being updated when the error occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkResultError,
        Level = LogLevel.Error,
        Message = "Error processing bulk result for saga {SagaId}" )]
    private static partial void LogBulkResultError( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs that a rate limit was encountered on a bulk endpoint.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="endpoint">The bulk endpoint that was rate-limited.</param>
    /// <param name="retryAfter">The retry-after value reported by the provider.</param>
    [LoggerMessage(
        EventId = LogEventIds.RateLimitEncountered,
        Level = LogLevel.Warning,
        Message = "Rate limit encountered on bulk endpoint {Endpoint}, retry after {RetryAfter}" )]
    private static partial void LogRateLimitEncountered( ILogger logger, string endpoint, string retryAfter );

    /// <summary>Logs that a saga was marked partial because a bulk endpoint was rate-limited.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The saga marked partial.</param>
    /// <param name="endpoint">The rate-limited endpoint.</param>
    /// <param name="retryAfter">The time until which the endpoint is rate-limited.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkSagaMarkedPartial,
        Level = LogLevel.Information,
        Message = "Bulk saga {SagaId} marked partial due to rate limit on {Endpoint} (retry after {RetryAfter})" )]
    private static partial void LogBulkSagaMarkedPartial( ILogger logger, string sagaId, string endpoint, DateTimeOffset retryAfter );

    /// <summary>Logs an error while requeuing a single message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="messageId">The composite id of the message that failed to requeue.</param>
    [LoggerMessage(
        EventId = LogEventIds.RequeueError,
        Level = LogLevel.Error,
        Message = "Error requeuing message {MessageId}" )]
    private static partial void LogRequeueError( ILogger logger, Exception ex, string messageId );

    /// <summary>Logs that the whole current batch is being requeued.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of messages being requeued.</param>
    /// <param name="reason">The reason the batch is being requeued.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkDispatchRequeuingAll,
        Level = LogLevel.Warning,
        Message = "Bulk dispatch: requeuing all {Count} messages, reason: {Reason}" )]
    private static partial void LogBulkDispatchRequeuingAll( ILogger logger, int count, string reason );

    /// <summary>Logs that a single message is being requeued.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="id">The lookup id whose message is being requeued.</param>
    /// <param name="reason">The reason the message is being requeued.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkDispatchRequeuingOne,
        Level = LogLevel.Warning,
        Message = "Bulk dispatch: requeuing message for id={Id}, reason: {Reason}" )]
    private static partial void LogBulkDispatchRequeuingOne( ILogger logger, string id, string reason );

    /// <summary>Logs that a saga became complete and a completion event is being published.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="sagaId">The completed saga.</param>
    [LoggerMessage(
        EventId = LogEventIds.SagaComplete,
        Level = LogLevel.Information,
        Message = "Saga {SagaId} is complete, publishing completion event" )]
    private static partial void LogSagaComplete( ILogger logger, string sagaId );

    /// <summary>Logs a failure to check or publish saga completion.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="sagaId">The saga whose completion check failed.</param>
    [LoggerMessage(
        EventId = LogEventIds.SagaCompletionCheckError,
        Level = LogLevel.Error,
        Message = "Failed to check/publish saga completion for {SagaId}" )]
    private static partial void LogSagaCompletionCheckError( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs a failure to publish a per-lookup completion event.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="sagaId">The saga whose lookup completion failed to publish.</param>
    [LoggerMessage(
        EventId = LogEventIds.PublishCompletionError,
        Level = LogLevel.Error,
        Message = "Failed to publish lookup completion for saga {SagaId}" )]
    private static partial void LogPublishCompletionError( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs a failure to publish the rate-limit sentinel for a saga.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="sagaId">The saga whose sentinel failed to publish.</param>
    [LoggerMessage(
        EventId = LogEventIds.BulkPublishRateLimitSentinelError,
        Level = LogLevel.Error,
        Message = "Failed to publish rate-limit sentinel for saga {SagaId}" )]
    private static partial void LogBulkPublishRateLimitSentinelError( ILogger logger, Exception ex, string sagaId );

    #endregion
}
