using System.Globalization;
using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using BridgeBeats.Worker.Spotify.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Redis Streams helper for the Spotify bulk-id lookup path: the dedicated track-id and album-id
/// bulk streams that <see cref="SpotifyBulkProcessorService"/> drains in batches.
/// </summary>
/// <remarks>
/// Single-id Spotify lookups are diverted onto dedicated bulk streams so they can be fetched in
/// multi-id batches. This helper owns the consumer-group lifecycle for those streams, depth and
/// age inspection (to drive flush decisions), batch dequeue (including reclaiming stalled pending
/// entries via XAUTOCLAIM), acknowledgement, and bounded requeue. Poison entries (missing or
/// unparseable payloads) are acknowledged and deleted rather than retried.
/// </remarks>
public sealed partial class SpotifyBatchQueueHelper {

    /// <summary>Redis connection multiplexer used for all stream operations.</summary>
    private readonly IConnectionMultiplexer _redis;

    /// <summary>Logger for this helper's structured log events.</summary>
    private readonly ILogger<SpotifyBatchQueueHelper> _logger;

    /// <summary>JSON options used to serialize and deserialize queued request payloads (camelCase, compact).</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Name of the Redis consumer group shared by the Spotify bulk streams.</summary>
    private const string ConsumerGroup = "spotify-workers";

    /// <summary>
    /// This process instance's consumer id within <see cref="ConsumerGroup"/>, derived from
    /// <see cref="Environment.MachineName"/> and a GUID then truncated to 32 characters via
    /// <c>[..32]</c>. The truncation discards part of the GUID, so this value is <b>not</b>
    /// guaranteed to be unique across multiple replicas running on the same host.
    /// </summary>
    private readonly string _consumerId;

    /// <summary>
    /// Minimum idle time (ms) before XAUTOCLAIM reclaims a pending entry.
    /// 60 seconds is long enough that a slow-but-alive processing cycle is
    /// never inadvertently reclaimed, while short enough that a crashed consumer
    /// does not block the stream for more than one minute.
    /// </summary>
    private const int AutoClaimMinIdleMs = 60_000;

    /// <summary>
    /// Initializes the helper, deriving this process instance's consumer id.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">The logger for this helper.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redis"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public SpotifyBatchQueueHelper(
        IConnectionMultiplexer redis,
        ILogger<SpotifyBatchQueueHelper> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );

        string uniqueId = Guid.NewGuid( ).ToString( "N" );
        _consumerId = $"spotify-batch-{Environment.MachineName}-{uniqueId}"[..32];

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Ensures the consumer group exists on both the bulk track-id and bulk album-id streams,
    /// creating each stream if absent.
    /// </summary>
    /// <param name="cancellationToken">Token used to stop between streams.</param>
    /// <returns>A task that completes once both groups are ensured.</returns>
    /// <remarks>
    /// A <c>BUSYGROUP</c> server error (the group already exists) is treated as success and logged
    /// at debug level.
    /// </remarks>
    public async Task EnsureConsumerGroupsAsync( CancellationToken cancellationToken = default ) {
        IDatabase db = _redis.GetDatabase( );

        foreach (string stream in new[] { SpotifyConstants.BulkTrackIdStream, SpotifyConstants.BulkAlbumIdStream }) {
            if (cancellationToken.IsCancellationRequested) { break; }
            try {
                _ = await db.StreamCreateConsumerGroupAsync(
                    stream,
                    ConsumerGroup,
                    StreamPosition.Beginning,
                    createStream: true
                );
                LogConsumerGroupCreated( _logger, ConsumerGroup, stream );
            } catch (RedisServerException ex) when (ex.Message.Contains( "BUSYGROUP" )) {
                LogConsumerGroupExists( _logger, ConsumerGroup, stream );
            }
        }
    }

    /// <summary>
    /// Reads the current depth of the bulk track-id and album-id streams.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>
    /// An <see cref="IdLookupDepth"/> reporting the bulk track and album stream lengths. The
    /// non-bulk count fields mirror the bulk counts for this helper.
    /// </returns>
    public async Task<IdLookupDepth> GetIdLookupDepthAsync( CancellationToken cancellationToken = default ) {
        cancellationToken.ThrowIfCancellationRequested( );
        IDatabase db = _redis.GetDatabase( );

        long bulkTrackCount = await db.StreamLengthAsync( SpotifyConstants.BulkTrackIdStream );
        long bulkAlbumCount = await db.StreamLengthAsync( SpotifyConstants.BulkAlbumIdStream );

        return new IdLookupDepth(
            TrackIdCount: (int)bulkTrackCount,
            AlbumIdCount: (int)bulkAlbumCount,
            BulkTrackIdCount: (int)bulkTrackCount,
            BulkAlbumIdCount: (int)bulkAlbumCount
        );
    }

    /// <summary>
    /// Returns the enqueue timestamp of the oldest entry in a bulk stream, used to decide whether
    /// the batch linger window has elapsed.
    /// </summary>
    /// <param name="isTracks"><see langword="true"/> to inspect the bulk track-id stream; <see langword="false"/> for the album-id stream.</param>
    /// <param name="cancellationToken">Token used to skip the read.</param>
    /// <returns>
    /// The oldest entry's enqueue time, or <see langword="null"/> when the stream is empty, the
    /// <c>enqueuedAt</c> field is absent or malformed, or a Redis read error occurs.
    /// </returns>
    /// <remarks>
    /// A malformed <c>enqueuedAt</c> value is logged and treated as absent (returns
    /// <see langword="null"/>) so a bad timestamp cannot wedge the flush decision.
    /// </remarks>
    public async Task<DateTimeOffset?> GetOldestEnqueuedAtAsync( bool isTracks, CancellationToken cancellationToken = default ) {
        if (cancellationToken.IsCancellationRequested) {
            return null;
        }

        string stream = isTracks ? SpotifyConstants.BulkTrackIdStream : SpotifyConstants.BulkAlbumIdStream;
        IDatabase db = _redis.GetDatabase( );

        try {
            // Redis streams are time-ordered: the first entry is always the oldest.
            StreamEntry[] entries = await db.StreamRangeAsync( stream, "-", "+", count: 1 );
            if (entries.Length == 0) {
                return null;
            }

            string? enqueuedAtStr = entries[0][QueueStreamFieldNames.EnqueuedAt];
            if (string.IsNullOrEmpty( enqueuedAtStr )) {
                return null;
            }

            if (!DateTimeOffset.TryParseExact( enqueuedAtStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed )) {
                // Malformed enqueuedAt — treat as absent so the age trigger does not strand a low-volume stream.
                LogMalformedEnqueuedAt( _logger, enqueuedAtStr, stream );
                return null;
            }

            return parsed;
        } catch (RedisServerException) {
            return null;
        }
    }

    /// <summary>
    /// Dequeues a batch of track-id lookup messages from the bulk track-id stream.
    /// </summary>
    /// <param name="maxCount">Maximum number of messages to claim; defaults to the configured per-batch track maximum.</param>
    /// <param name="cancellationToken">Token used to stop dequeuing early.</param>
    /// <returns>The claimed messages, which may be fewer than requested (including empty).</returns>
    public async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> DequeueTrackIdBatchAsync(
        int? maxCount = null,
        CancellationToken cancellationToken = default
    ) {
        int count = maxCount ?? SpotifyConstants.MaxTracksPerBatchLookup;
        return await DequeueBatchFromStreamAsync( SpotifyConstants.BulkTrackIdStream, count, cancellationToken );
    }

    /// <summary>
    /// Dequeues a batch of album-id lookup messages from the bulk album-id stream.
    /// </summary>
    /// <param name="maxCount">Maximum number of messages to claim; defaults to the configured per-batch album maximum.</param>
    /// <param name="cancellationToken">Token used to stop dequeuing early.</param>
    /// <returns>The claimed messages, which may be fewer than requested (including empty).</returns>
    public async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> DequeueAlbumIdBatchAsync(
        int? maxCount = null,
        CancellationToken cancellationToken = default
    ) {
        int count = maxCount ?? SpotifyConstants.MaxAlbumsPerBatchLookup;
        return await DequeueBatchFromStreamAsync( SpotifyConstants.BulkAlbumIdStream, count, cancellationToken );
    }

    /// <summary>
    /// Dequeues up to <paramref name="count"/> messages from a bulk stream, first reclaiming
    /// stalled pending entries via XAUTOCLAIM and then reading new entries for this consumer.
    /// </summary>
    /// <param name="stream">The bulk stream key to read from.</param>
    /// <param name="count">Maximum number of messages to return.</param>
    /// <param name="cancellationToken">Token used to stop reading early.</param>
    /// <returns>The decoded messages in claim order; poison entries are dropped, not returned.</returns>
    /// <remarks>
    /// Reclaimed entries are counted against <paramref name="count"/> so a single call never
    /// exceeds the requested batch size. Entries with missing or unparseable payloads are
    /// acknowledged and deleted as poison. If XAUTOCLAIM is unsupported by the server, the reclaim
    /// step is skipped and only new entries are read.
    /// </remarks>
    private async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> DequeueBatchFromStreamAsync(
        string stream,
        int count,
        CancellationToken cancellationToken
    ) {
        IDatabase db = _redis.GetDatabase( );
        List<QueuedMessage<QueuedLookupRequest>> messages = [];

        // XAUTOCLAIM sweep: reclaim any PEL entries idle longer than AutoClaimMinIdleMs,
        // then immediately deserialize and append them to the result — claimed entries land
        // in THIS consumer's PEL and XREADGROUP with ">" can never see them again.
        // Safe here because there is exactly one consumer service (SpotifyBulkProcessorService)
        // reading these streams — no other consumer group member can be legitimately processing
        // the entry after 60 seconds of idle time.
        int remainingCount = count;
        try {
            StreamAutoClaimResult claimed = await db.StreamAutoClaimAsync(
                stream,
                ConsumerGroup,
                _consumerId,
                AutoClaimMinIdleMs,
                "0-0",
                count
            );

            if (claimed.ClaimedEntries.Length > 0) {
                LogAutoClaimRecovered( _logger, claimed.ClaimedEntries.Length, stream );

                foreach (StreamEntry entry in claimed.ClaimedEntries) {
                    if (cancellationToken.IsCancellationRequested || remainingCount <= 0) { break; }

                    string? payload = entry[QueueStreamFieldNames.Payload];
                    string? enqueuedAtStr = entry[QueueStreamFieldNames.EnqueuedAt];

                    if (string.IsNullOrEmpty( payload )) {
                        // Poison: no payload field — ACK+XDEL to prevent infinite re-claim loop.
                        await AckAndDeletePoisonEntryAsync( db, stream, entry.Id.ToString( ) );
                        continue;
                    }

                    try {
                        QueuedLookupRequest? request = JsonSerializer.Deserialize<QueuedLookupRequest>( payload, _jsonOptions );
                        if (request is null) {
                            // Literal JSON null payload — deserializes without throwing but produces
                            // a null object. Route through AckAndDeletePoisonEntryAsync so this entry
                            // is removed from the PEL rather than remaining for eternal 60s re-claims.
                            LogDeserializationError( _logger, new InvalidOperationException( "null payload" ), entry.Id.ToString( ), stream );
                            await AckAndDeletePoisonEntryAsync( db, stream, entry.Id.ToString( ) );
                            continue;
                        }

                        DateTimeOffset enqueuedAt = DateTimeOffset.UtcNow;
                        if (!string.IsNullOrEmpty( enqueuedAtStr ) &&
                            !DateTimeOffset.TryParseExact( enqueuedAtStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out enqueuedAt )) {
                            LogMalformedEnqueuedAt( _logger, enqueuedAtStr, stream );
                            enqueuedAt = DateTimeOffset.UtcNow;
                        }

                        string compositeId = $"{stream}:{entry.Id}";
                        messages.Add( new QueuedMessage<QueuedLookupRequest>( compositeId, request, enqueuedAt ) );
                        remainingCount--;
                    } catch (JsonException ex) {
                        // Poison message: cannot deserialize even after re-claim — ACK and delete
                        // to prevent infinite re-claim/re-fail every AutoClaimMinIdleMs.
                        LogDeserializationError( _logger, ex, entry.Id.ToString( ), stream );
                        await AckAndDeletePoisonEntryAsync( db, stream, entry.Id.ToString( ) );
                    }
                }
            }
        } catch (RedisServerException ex) {
            // XAUTOCLAIM was added in Redis 6.2; if the server is older, log and continue
            LogAutoClaimNotSupported( _logger, ex, stream );
        }

        // Skip XREADGROUP if the claimed entries already filled the budget.
        if (remainingCount <= 0) {
            return messages;
        }

        try {
            StreamEntry[] entries = await db.StreamReadGroupAsync(
                stream,
                ConsumerGroup,
                _consumerId,
                count: remainingCount,
                noAck: false
            );

            foreach (StreamEntry entry in entries) {
                if (cancellationToken.IsCancellationRequested) { break; }
                string? payload = entry[QueueStreamFieldNames.Payload];
                string? enqueuedAtStr = entry[QueueStreamFieldNames.EnqueuedAt];

                if (string.IsNullOrEmpty( payload )) {
                    // Poison: no payload field — ACK+XDEL to remove from PEL so it cannot be
                    // re-read by XREADGROUP or re-claimed by XAUTOCLAIM every AutoClaimMinIdleMs.
                    await AckAndDeletePoisonEntryAsync( db, stream, entry.Id.ToString( ) );
                    continue;
                }

                try {
                    QueuedLookupRequest? request = JsonSerializer.Deserialize<QueuedLookupRequest>( payload, _jsonOptions );
                    if (request is null) {
                        // Literal JSON null payload — deserializes without throwing but produces
                        // a null object. Route through AckAndDeletePoisonEntryAsync so this entry
                        // is removed from the PEL rather than remaining for eternal 60s re-claims.
                        LogDeserializationError( _logger, new InvalidOperationException( "null payload" ), entry.Id.ToString( ), stream );
                        await AckAndDeletePoisonEntryAsync( db, stream, entry.Id.ToString( ) );
                        continue;
                    }

                    DateTimeOffset enqueuedAt = DateTimeOffset.UtcNow;
                    if (!string.IsNullOrEmpty( enqueuedAtStr ) &&
                        !DateTimeOffset.TryParseExact( enqueuedAtStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out enqueuedAt )) {
                        LogMalformedEnqueuedAt( _logger, enqueuedAtStr, stream );
                        enqueuedAt = DateTimeOffset.UtcNow;
                    }

                    string compositeId = $"{stream}:{entry.Id}";
                    messages.Add( new QueuedMessage<QueuedLookupRequest>( compositeId, request, enqueuedAt ) );
                } catch (JsonException ex) {
                    // Poison message: ACK and delete to remove it from the PEL so it cannot be
                    // re-claimed by XAUTOCLAIM and re-fail every AutoClaimMinIdleMs indefinitely.
                    LogDeserializationError( _logger, ex, entry.Id.ToString( ), stream );
                    await AckAndDeletePoisonEntryAsync( db, stream, entry.Id.ToString( ) );
                }
            }
        } catch (RedisServerException ex) {
            LogStreamReadWarning( _logger, ex, stream );
        }

        return messages;
    }

    /// <summary>
    /// Acknowledges and deletes a single poison entry (missing or unparseable payload) so it is
    /// never re-read.
    /// </summary>
    /// <param name="db">The Redis database to operate on.</param>
    /// <param name="stream">The stream the entry belongs to.</param>
    /// <param name="id">The Redis entry id to acknowledge and delete.</param>
    /// <returns>A task that completes once the entry is removed (Redis errors are logged and swallowed).</returns>
    private async Task AckAndDeletePoisonEntryAsync( IDatabase db, string stream, string id ) {
        try {
            _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
            _ = await db.StreamDeleteAsync( stream, [id] );
        } catch (RedisServerException ex) {
            LogStreamReadWarning( _logger, ex, stream );
        }
    }

    /// <summary>
    /// Acknowledges and deletes a processed message from its bulk stream.
    /// </summary>
    /// <param name="messageId">The composite message id (<c>"{stream}:{redisId}"</c>) to acknowledge.</param>
    /// <returns>A task that completes once the message is acknowledged and deleted.</returns>
    /// <remarks>Acknowledgement deletes the entry (XACK + XDEL); processed messages are not retained in the stream.</remarks>
    public async Task AcknowledgeAsync( string messageId ) {
        (string stream, string id) = ParseMessageId( messageId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        LogMessageAcknowledged( _logger, id, stream );
    }

    /// <summary>
    /// Requeues a bulk-stream message with an incremented attempt count, enforcing the retry cap.
    /// </summary>
    /// <param name="messageId">The composite message id (<c>"{stream}:{redisId}"</c>) to requeue.</param>
    /// <param name="sagaId">The saga id associated with the message, used for diagnostic logging.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see cref="RequeueOutcome.Requeued"/> when the message was re-added with an incremented
    /// attempt; <see cref="RequeueOutcome.CapReached"/> when the retry cap was reached or the
    /// payload was unserializable (the message is discarded); or
    /// <see cref="RequeueOutcome.NotFound"/> when the original entry no longer exists.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before work begins.</exception>
    /// <remarks>
    /// The original entry is always acknowledged and deleted first; on a successful requeue a fresh
    /// entry is added with a new <c>enqueuedAt</c> timestamp and <c>AttemptCount + 1</c>. The caller
    /// is responsible for finalizing the saga when the cap is reached.
    /// </remarks>
    public async Task<RequeueOutcome> RequeueAsync(
        string messageId,
        string sagaId,
        CancellationToken cancellationToken = default
    ) {
        cancellationToken.ThrowIfCancellationRequested( );
        (string stream, string id) = ParseMessageId( messageId );

        IDatabase db = _redis.GetDatabase( );

        // Read the original message
        StreamEntry[] entries = await db.StreamRangeAsync( stream, id, id, count: 1 );

        if (entries.Length == 0) {
            // Entry already XDELed — duplicate in-flight after XAUTOCLAIM re-claim.
            // Log and signal caller to leave saga untouched (another consumer processed it).
            LogMessageNotFound( _logger, id, stream );
            return RequeueOutcome.NotFound;
        }

        StreamEntry original = entries[0];
        string? rawPayload = original[QueueStreamFieldNames.Payload];

        // Deserialize to get current AttemptCount
        QueuedLookupRequest? request = null;
        if (!string.IsNullOrEmpty( rawPayload )) {
            try {
                request = JsonSerializer.Deserialize<QueuedLookupRequest>( rawPayload, _jsonOptions );
            } catch (JsonException) {
                // Fall through — will requeue with attempt 1 if we can't parse
            }
        }

        int currentAttempt = request?.AttemptCount ?? 0;

        // ACK and delete the original entry in both paths
        _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        if (currentAttempt >= LookupConstants.MaxQueueRetryAttempts) {
            // Cap reached: log and signal caller to handle saga completion.
            // The message is already ACK+deleted above (removed from the PEL and stream).
            // Caller must write the failed provider state and publish completion events.
            LogMaxRetriesExceeded( _logger, id, LookupConstants.MaxQueueRetryAttempts, sagaId );
            return RequeueOutcome.CapReached;
        }

        if (request is null) {
            // Payload could not be deserialized — cannot safely increment AttemptCount.
            // Drop the message and signal CapReached so the caller writes failed saga state.
            LogPoisonPayloadDiscarded( _logger, id, sagaId );
            return RequeueOutcome.CapReached;
        }

        // Build re-serialized payload with AttemptCount + 1
        QueuedLookupRequest requeuedRequest = request with { AttemptCount = currentAttempt + 1 };

        string newPayload = JsonSerializer.Serialize( requeuedRequest, _jsonOptions );

        NameValueEntry[] fields = [
            new NameValueEntry( QueueStreamFieldNames.Payload, newPayload ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( stream, fields );

        LogMessageRequeued( _logger, stream, currentAttempt + 1 );
        return RequeueOutcome.Requeued;
    }

    /// <summary>
    /// Splits a composite message id into its stream key and Redis entry id.
    /// </summary>
    /// <param name="compositeId">The composite id, normally <c>"{stream}:{redisId}"</c>.</param>
    /// <returns>A tuple of the stream key and the Redis entry id.</returns>
    /// <exception cref="ArgumentException">Thrown when the id does not match a known bulk stream prefix and has no parseable colon separator.</exception>
    /// <remarks>
    /// Known bulk stream prefixes are matched first (their keys themselves contain colons); the
    /// last-colon fallback handles any other well-formed composite id.
    /// </remarks>
    private static (string stream, string id) ParseMessageId( string compositeId ) {
        // Format: stream:id where id may contain colons (Redis stream IDs are timestamp-sequence)
        foreach (string streamPrefix in new[] { SpotifyConstants.BulkTrackIdStream, SpotifyConstants.BulkAlbumIdStream }) {
            if (compositeId.StartsWith( streamPrefix + ":", StringComparison.OrdinalIgnoreCase )) {
                string id = compositeId[(streamPrefix.Length + 1)..];
                return (streamPrefix, id);
            }
        }

        // Fallback: assume last segment after colon is the ID
        int lastColon = compositeId.LastIndexOf( ':' );
        return lastColon <= 0
            ? throw new ArgumentException( $"Invalid composite message ID: {compositeId}", nameof( compositeId ) )
            : ((string stream, string id))(compositeId[..lastColon], compositeId[(lastColon + 1)..]);
    }

    #region LoggerMessage Methods

    /// <summary>Logs creation of a consumer group on a bulk stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="group">The consumer group name.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.ConsumerGroupCreated,
        Level = LogLevel.Information,
        Message = "Created consumer group {Group} for stream {Stream}" )]
    private static partial void LogConsumerGroupCreated( ILogger logger, string group, string stream );

    /// <summary>Logs that a consumer group already existed on a bulk stream (benign).</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="group">The consumer group name.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.ConsumerGroupExists,
        Level = LogLevel.Debug,
        Message = "Consumer group {Group} already exists for stream {Stream}" )]
    private static partial void LogConsumerGroupExists( ILogger logger, string group, string stream );

    /// <summary>Logs that a request was routed onto a type-specific bulk stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="lookupType">The lookup type of the routed request.</param>
    /// <param name="stream">The destination bulk stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.EnqueuedToBulkStream,
        Level = LogLevel.Debug,
        Message = "Enqueued {LookupType} request to type-specific bulk stream {Stream}" )]
    private static partial void LogEnqueuedToBulkStream( ILogger logger, string lookupType, string stream );

    /// <summary>Logs that pending entries were reclaimed from a stream via XAUTOCLAIM.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of entries reclaimed.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.AutoClaimRecovered,
        Level = LogLevel.Information,
        Message = "XAUTOCLAIM recovered {Count} pending entries from {Stream}" )]
    private static partial void LogAutoClaimRecovered( ILogger logger, int count, string stream );

    /// <summary>Logs that XAUTOCLAIM is unsupported by the server, disabling pending-entry recovery.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The Redis error returned by the server.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.AutoClaimNotSupported,
        Level = LogLevel.Warning,
        Message = "XAUTOCLAIM not supported on {Stream} — pending-entry recovery disabled (requires Redis ≥ 6.2)" )]
    private static partial void LogAutoClaimNotSupported( ILogger logger, Exception ex, string stream );

    /// <summary>Logs that a stream entry payload failed to deserialize and was discarded.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The deserialization exception.</param>
    /// <param name="id">The Redis entry id.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.DeserializationError,
        Level = LogLevel.Error,
        Message = "Failed to deserialize message {Id} from {Stream}" )]
    private static partial void LogDeserializationError( ILogger logger, Exception ex, string id, string stream );

    /// <summary>Logs a non-fatal error while reading from a bulk stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.StreamReadWarning,
        Level = LogLevel.Warning,
        Message = "Error reading from stream {Stream}" )]
    private static partial void LogStreamReadWarning( ILogger logger, Exception ex, string stream );

    /// <summary>Logs that a message was acknowledged and deleted from its stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The Redis entry id acknowledged.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.MessageAcknowledged,
        Level = LogLevel.Debug,
        Message = "Acknowledged and deleted message {MessageId} from {Stream}" )]
    private static partial void LogMessageAcknowledged( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message targeted for requeue was not found.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The Redis entry id that was not found.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.MessageNotFound,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} not found in {Stream} for requeue" )]
    private static partial void LogMessageNotFound( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message was requeued with an incremented attempt count.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="stream">The stream key.</param>
    /// <param name="attempt">The new attempt count after requeue.</param>
    [LoggerMessage(
        EventId = LogEventIds.MessageRequeued,
        Level = LogLevel.Debug,
        Message = "Requeued message from {Stream} (attempt {Attempt})" )]
    private static partial void LogMessageRequeued( ILogger logger, string stream, int attempt );

    /// <summary>Logs that a message exceeded the retry cap and was discarded.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The Redis entry id discarded.</param>
    /// <param name="maxRetries">The retry-attempt cap that was reached.</param>
    /// <param name="sagaId">The saga id associated with the message.</param>
    [LoggerMessage(
        EventId = LogEventIds.MaxRetriesExceeded,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} in bulk stream exceeded {MaxRetries} retry attempts; discarding (saga={SagaId})" )]
    private static partial void LogMaxRetriesExceeded( ILogger logger, string messageId, int maxRetries, string sagaId );

    /// <summary>Logs that a message with an unserializable payload was discarded as poison.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The Redis entry id discarded.</param>
    /// <param name="sagaId">The saga id associated with the message.</param>
    [LoggerMessage(
        EventId = LogEventIds.PoisonPayloadDiscarded,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} in bulk stream has an unserializable payload; discarding (saga={SagaId})" )]
    private static partial void LogPoisonPayloadDiscarded( ILogger logger, string messageId, string sagaId );

    /// <summary>Logs that a malformed <c>enqueuedAt</c> field was encountered and treated as absent.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="enqueuedAtStr">The raw, unparseable timestamp value.</param>
    /// <param name="stream">The stream key.</param>
    [LoggerMessage(
        EventId = LogEventIds.MalformedEnqueuedAt,
        Level = LogLevel.Warning,
        Message = "Malformed enqueuedAt field '{EnqueuedAtStr}' in stream {Stream}; treating as absent" )]
    private static partial void LogMalformedEnqueuedAt( ILogger logger, string enqueuedAtStr, string stream );

    #endregion
}

/// <summary>
/// Snapshot of the Spotify bulk-id stream depths used to drive batch flush decisions.
/// </summary>
/// <param name="TrackIdCount">Number of pending track-id entries (mirrors <paramref name="BulkTrackIdCount"/>).</param>
/// <param name="AlbumIdCount">Number of pending album-id entries (mirrors <paramref name="BulkAlbumIdCount"/>).</param>
/// <param name="BulkTrackIdCount">Length of the bulk track-id stream.</param>
/// <param name="BulkAlbumIdCount">Length of the bulk album-id stream.</param>
public sealed record IdLookupDepth(
    int TrackIdCount,
    int AlbumIdCount,
    int BulkTrackIdCount,
    int BulkAlbumIdCount
);

/// <summary>
/// Result of a bulk-stream requeue attempt.
/// </summary>
public enum RequeueOutcome {
    /// <summary>The message was re-added to its stream with an incremented attempt count.</summary>
    Requeued,
    /// <summary>The retry cap was reached, or the payload was unserializable; the message was discarded.</summary>
    CapReached,
    /// <summary>The original entry no longer existed in the stream, so nothing was requeued.</summary>
    NotFound
}
