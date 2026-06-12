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
/// Background service that processes bulk ID lookups for Spotify when count or age thresholds are met.
/// </summary>
/// <remarks>
/// Processes type-specific bulk track/album ID streams. Size is the primary flush trigger;
/// the linger age is a staleness backstop, not a latency bound.
/// </remarks>
public sealed partial class SpotifyBulkProcessorService : BackgroundService {

    private readonly IConnectionMultiplexer _redis;
    private readonly SpotifyBatchQueueHelper _batchHelper;
    private readonly IRateLimitTracker _rateLimitTracker;
    private readonly ISagaStateManager _sagaManager;
    private readonly ISpotifyBulkLookupService _lookupService;
    private readonly ILogger<SpotifyBulkProcessorService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _batchLinger;
    private readonly int _requestFailureCooldownSeconds;

    // Request-failure cooldown state — suppresses flush after empty-dict (network/auth error)
    // to avoid burning retry attempts faster than Spotify can recover.
    // Cooldown doubles per consecutive failure, capped at SpotifyBatchSettings.MaxRequestFailureCooldownSeconds.
    // Each stream maintains its own independent cooldown: a tracks-endpoint failure does not
    // suppress album flushes (and vice versa), consistent with per-endpoint rate-limit isolation.
    private DateTimeOffset _trackIdCooldownUntil = DateTimeOffset.MinValue;
    private int _consecutiveTrackIdFailures;
    private DateTimeOffset _albumIdCooldownUntil = DateTimeOffset.MinValue;
    private int _consecutiveAlbumIdFailures;

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

    // -------------------------------------------------------------------------
    // Pure flush predicate — extracted for testability (M7a)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pure predicate: returns <see langword="true"/> when either the size threshold
    /// or the linger age threshold would trigger a flush.
    /// </summary>
    /// <param name="count">Number of messages currently in the stream.</param>
    /// <param name="oldestAge">
    /// Age of the oldest message in the stream, or <see langword="null"/> if the stream
    /// is empty or the enqueuedAt field is absent.
    /// </param>
    /// <param name="threshold">Minimum count for an immediate size flush.</param>
    /// <param name="linger">Maximum age before a linger flush fires.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="count"/> ≥ <paramref name="threshold"/>,
    /// or when <paramref name="count"/> &gt; 0 and <paramref name="oldestAge"/> ≥ <paramref name="linger"/>.
    /// </returns>
    public static bool ShouldFlush( int count, TimeSpan? oldestAge, int threshold, TimeSpan linger ) {
        if (count >= threshold) {
            return true;
        }
        return count > 0 && oldestAge.HasValue && oldestAge.Value >= linger;
    }

    // -------------------------------------------------------------------------
    // Flush-decision methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Checks if bulk track lookups should be processed (size-OR-age flush policy).
    /// </summary>
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

        // Request-failure cooldown guard (F7): suppress flush while in backoff after an
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
    /// Checks if bulk album lookups should be processed (size-OR-age flush policy).
    /// </summary>
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

        // Request-failure cooldown guard (F7)
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

    // -------------------------------------------------------------------------
    // Batch-processing methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Processes a batch of track ID lookups from the type-specific bulk stream.
    /// </summary>
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

            // B1 fix: empty dictionary = request failure (auth error, network error).
            // Requeue ALL messages without writing any saga state. Arm the request-failure
            // cooldown so the linger-trigger does not immediately re-flush (F7).
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
                        // B1 fix: key absent from non-empty dict = partial parse failure.
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
    /// Processes a batch of album ID lookups from the type-specific bulk stream.
    /// </summary>
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

            // B1 fix: empty dictionary = request failure → requeue ALL. Arm cooldown (F7).
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
                        // B1 fix: absent key in non-empty dict = partial parse failure → requeue one
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
    /// Processes the result for a single message from a bulk lookup.
    /// </summary>
    /// <remarks>
    /// Mirrors the generic worker's saga write order: GetOrCreate (materializes JetStream fire-and-forget sagas)
    /// -&gt; InitializeProviderStates -&gt; UpdateProviderState -&gt; publish completion.
    /// Only the key-present case reaches here.
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

    // -------------------------------------------------------------------------
    // Rate-limit and requeue helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records rate-limit state, marks each saga partial, merges rate-limit info, and publishes
    /// the rate-limited sentinel so interactive callers do not hang on a 429.
    /// </summary>
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
    /// Requeues all messages in a batch, handling the retry cap for each one.
    /// </summary>
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
    /// Requeues a single message.
    /// <list type="bullet">
    ///   <item><see cref="RequeueOutcome.CapReached"/> — writes the failed provider state and
    ///   publishes completion events so saga coordinators and interactive waiters unblock (F2).</item>
    ///   <item><see cref="RequeueOutcome.NotFound"/> — entry already XDELed (duplicate in-flight
    ///   after re-claim); saga is left untouched because another consumer already processed it.</item>
    ///   <item><see cref="RequeueOutcome.Requeued"/> — no saga write needed.</item>
    /// </list>
    /// </summary>
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
    /// Arms the request-failure flush-suppression cooldown for a stream.
    /// The cooldown doubles per consecutive failure, capped at
    /// <see cref="SpotifyBatchSettings.MaxRequestFailureCooldownSeconds"/>.
    /// </summary>
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
    /// Pure function: computes the cooldown duration in seconds for a given consecutive-failure count.
    /// Exponential backoff: <paramref name="baseSeconds"/> × 2^(n−1), clamped at
    /// <paramref name="maxSeconds"/>. The exponent is clamped to 5 before the left-shift to prevent
    /// integer overflow when <paramref name="consecutiveFailures"/> is large.
    /// </summary>
    /// <param name="consecutiveFailures">Number of consecutive failures (1-based; must be ≥ 1).</param>
    /// <param name="baseSeconds">Base cooldown in seconds.</param>
    /// <param name="maxSeconds">Maximum cooldown in seconds.</param>
    /// <returns>Cooldown seconds, always &gt; 0 and ≤ <paramref name="maxSeconds"/>.</returns>
    internal static int ComputeCooldownSeconds( int consecutiveFailures, int baseSeconds, int maxSeconds ) {
        int exp = Math.Min( consecutiveFailures - 1, 5 );
        return Math.Min( baseSeconds * (1 << exp), maxSeconds );
    }

    // -------------------------------------------------------------------------
    // Saga completion and pub/sub helpers
    // -------------------------------------------------------------------------

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
    /// Publishes a lookup completion event so the SagaCoordinator can process via pattern subscription.
    /// </summary>
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
    /// Publishes the rate-limited sentinel so interactive callers receive a partial result
    /// immediately rather than waiting for the full timeout.
    /// </summary>
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

    /// <summary>Logs that a saga was marked partial due to a bulk rate limit.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkSagaMarkedPartial,
        Level = LogLevel.Information,
        Message = "Bulk saga {SagaId} marked partial due to rate limit on {Endpoint} (retry after {RetryAfter})" )]
    private static partial void LogBulkSagaMarkedPartial( ILogger logger, string sagaId, string endpoint, DateTimeOffset retryAfter );

    /// <summary>Logs an error requeuing a message.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RequeueError,
        Level = LogLevel.Error,
        Message = "Error requeuing message {MessageId}" )]
    private static partial void LogRequeueError( ILogger logger, Exception ex, string messageId );

    /// <summary>Logs that the dispatch is requeuing all messages due to an empty result dict (B1).</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkDispatchRequeuingAll,
        Level = LogLevel.Warning,
        Message = "Bulk dispatch: requeuing all {Count} messages, reason: {Reason}" )]
    private static partial void LogBulkDispatchRequeuingAll( ILogger logger, int count, string reason );

    /// <summary>Logs that a single message is being requeued due to an absent key in a non-empty result dict (B1).</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkDispatchRequeuingOne,
        Level = LogLevel.Warning,
        Message = "Bulk dispatch: requeuing message for id={Id}, reason: {Reason}" )]
    private static partial void LogBulkDispatchRequeuingOne( ILogger logger, string id, string reason );

    /// <summary>Logs that a saga is complete, publishing completion event.</summary>
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
        Message = "Failed to publish lookup completion for saga {SagaId}" )]
    private static partial void LogPublishCompletionError( ILogger logger, Exception ex, string sagaId );

    /// <summary>Logs an error publishing the rate-limit sentinel.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BulkPublishRateLimitSentinelError,
        Level = LogLevel.Error,
        Message = "Failed to publish rate-limit sentinel for saga {SagaId}" )]
    private static partial void LogBulkPublishRateLimitSentinelError( ILogger logger, Exception ex, string sagaId );

    #endregion
}
