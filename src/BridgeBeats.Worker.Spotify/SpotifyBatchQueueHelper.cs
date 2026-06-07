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
/// Spotify-specific batch queue helper that manages type-specific bulk streams
/// for track and album ID lookups consumed by <c>SpotifyBulkProcessorService</c>.
/// </summary>
/// <remarks>
/// <para>
/// Stream naming convention (constants in <see cref="SpotifyConstants"/>):
/// <list type="bullet">
///   <item><c>queue:spotify:bulk:track-id</c> — <see cref="LookupRequestType.SongIdLookup"/> bulk messages.</item>
///   <item><c>queue:spotify:bulk:album-id</c> — <see cref="LookupRequestType.AlbumIdLookup"/> bulk messages.</item>
/// </list>
/// </para>
/// <para>
/// Pending-entry (PEL) recovery: at the start of each <see cref="DequeueBatchFromStreamAsync"/>
/// call an <c>XAUTOCLAIM</c> sweep reclaims entries that have been idle in the PEL
/// for more than <see cref="AutoClaimMinIdleMs"/> milliseconds. This prevents crash-between-read-and-ACK
/// from stranding messages in a dead consumer's PEL indefinitely, which would otherwise
/// cause the age trigger to fire every 500ms against empty dequeues (the project's
/// known log-flood failure mode).
/// </para>
/// </remarks>
public sealed partial class SpotifyBatchQueueHelper {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SpotifyBatchQueueHelper> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string ConsumerGroup = "spotify-workers";
    private readonly string _consumerId;

    /// <summary>
    /// Minimum idle time (ms) before XAUTOCLAIM reclaims a pending entry.
    /// 60 seconds is long enough that a slow-but-alive processing cycle is
    /// never inadvertently reclaimed, while short enough that a crashed consumer
    /// does not block the stream for more than one minute.
    /// </summary>
    private const int AutoClaimMinIdleMs = 60_000;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyBatchQueueHelper"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
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
    /// Ensures consumer groups exist for the type-specific bulk streams.
    /// Call this during worker startup.
    /// </summary>
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
    /// Gets the current depth of ID-based lookup requests in the type-specific bulk streams.
    /// Returns the XLEN of each stream — no deserialization, no stray scans.
    /// </summary>
    /// <returns>A record containing track and album bulk-stream lengths.</returns>
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
    /// Gets the <see cref="DateTimeOffset"/> of the oldest message in one of the type-specific
    /// bulk streams, used by the size-OR-age flush policy in
    /// <c>SpotifyBulkProcessorService</c>.
    /// </summary>
    /// <param name="isTracks">
    /// <see langword="true"/> to check <c>queue:spotify:bulk:track-id</c>;
    /// <see langword="false"/> to check <c>queue:spotify:bulk:album-id</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <c>enqueuedAt</c> timestamp of the oldest entry, or <see langword="null"/>
    /// if the stream is empty or the field is absent.
    /// </returns>
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
            return !string.IsNullOrEmpty( enqueuedAtStr )
                ? DateTimeOffset.ParseExact( enqueuedAtStr, "O", CultureInfo.InvariantCulture )
                : null;
        } catch (RedisServerException) {
            return null;
        }
    }

    /// <summary>
    /// Dequeues a batch of track ID lookup requests.
    /// </summary>
    /// <param name="maxCount">Maximum number of requests to dequeue (defaults to <see cref="SpotifyConstants.MaxTracksPerBatchLookup"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of queued messages.</returns>
    public async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> DequeueTrackIdBatchAsync(
        int? maxCount = null,
        CancellationToken cancellationToken = default
    ) {
        int count = maxCount ?? SpotifyConstants.MaxTracksPerBatchLookup;
        return await DequeueBatchFromStreamAsync( SpotifyConstants.BulkTrackIdStream, count, cancellationToken );
    }

    /// <summary>
    /// Dequeues a batch of album ID lookup requests.
    /// </summary>
    /// <param name="maxCount">Maximum number of requests to dequeue (defaults to <see cref="SpotifyConstants.MaxAlbumsPerBatchLookup"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of queued messages.</returns>
    public async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> DequeueAlbumIdBatchAsync(
        int? maxCount = null,
        CancellationToken cancellationToken = default
    ) {
        int count = maxCount ?? SpotifyConstants.MaxAlbumsPerBatchLookup;
        return await DequeueBatchFromStreamAsync( SpotifyConstants.BulkAlbumIdStream, count, cancellationToken );
    }

    /// <summary>
    /// Dequeues a batch of messages from a specific stream.
    /// </summary>
    /// <remarks>
    /// Runs an <c>XAUTOCLAIM</c> sweep before XREADGROUP so entries stranded in a dead
    /// consumer's PEL (crash between read and ACK) are recovered automatically.
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

                        DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                            ? DateTimeOffset.ParseExact( enqueuedAtStr, "O", CultureInfo.InvariantCulture )
                            : DateTimeOffset.UtcNow;

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

                    DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                        ? DateTimeOffset.ParseExact( enqueuedAtStr, "O", CultureInfo.InvariantCulture )
                        : DateTimeOffset.UtcNow;

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
    /// ACKs and XDELs an entry that cannot be deserialized (poison message), removing it
    /// from the PEL so it is not endlessly re-claimed by XAUTOCLAIM.
    /// </summary>
    private async Task AckAndDeletePoisonEntryAsync( IDatabase db, string stream, string id ) {
        try {
            _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
            _ = await db.StreamDeleteAsync( stream, [id] );
        } catch (RedisServerException ex) {
            LogStreamReadWarning( _logger, ex, stream );
        }
    }

    /// <summary>
    /// Acknowledges and removes a message from its stream.
    /// </summary>
    /// <param name="messageId">The composite message ID (stream:id format).</param>
    public async Task AcknowledgeAsync( string messageId ) {
        (string stream, string id) = ParseMessageId( messageId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        LogMessageAcknowledged( _logger, id, stream );
    }

    /// <summary>
    /// Requeues a message to its original stream, incrementing the attempt count.
    /// </summary>
    /// <remarks>
    /// When the attempt count reaches <see cref="LookupConstants.MaxQueueRetryAttempts"/>,
    /// the message is ACKed and deleted without re-adding, and this method returns
    /// <see cref="RequeueOutcome.CapReached"/> to signal the caller to write failed saga state.
    /// When the entry is not found in the stream (e.g. already XDELed — duplicate in-flight
    /// after XAUTOCLAIM re-claim), this method returns <see cref="RequeueOutcome.NotFound"/>
    /// and the caller must leave the saga untouched (it was processed by another consumer).
    /// The caller is responsible for writing the complete-failed saga state and
    /// publishing the completion events for <see cref="RequeueOutcome.CapReached"/> —
    /// keeping service-layer saga writes out of this infrastructure helper.
    /// <para>
    /// DLQ note: the bulk streams do not have a reference to the underlying
    /// <c>IRequestQueue</c> instance, so <c>MoveToDlqAsync</c> is not reachable from
    /// this helper. The ACK+XDEL in the cap path removes the message from the stream;
    /// this helper's own WARNING log (<see cref="LogMaxRetriesExceeded"/>) is the audit
    /// trail. If DLQ access is needed in future, thread the queue reference through.
    /// </para>
    /// </remarks>
    /// <param name="messageId">The composite message ID (stream:id format).</param>
    /// <param name="sagaId">The saga ID of the associated request (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="RequeueOutcome.Requeued"/> if the message was re-queued for another attempt;
    /// <see cref="RequeueOutcome.CapReached"/> if the retry cap was reached and the message was discarded;
    /// <see cref="RequeueOutcome.NotFound"/> if the entry was absent from the stream (already XDELed).
    /// </returns>
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
            // Drop and signal caller so the saga is not left incomplete.
            LogMaxRetriesExceeded( _logger, id, 0, sagaId );
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

    /// <summary>Logs that a consumer group was created.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ConsumerGroupCreated,
        Level = LogLevel.Information,
        Message = "Created consumer group {Group} for stream {Stream}" )]
    private static partial void LogConsumerGroupCreated( ILogger logger, string group, string stream );

    /// <summary>Logs that a consumer group already exists.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ConsumerGroupExists,
        Level = LogLevel.Debug,
        Message = "Consumer group {Group} already exists for stream {Stream}" )]
    private static partial void LogConsumerGroupExists( ILogger logger, string group, string stream );

    /// <summary>Logs that a request was enqueued to a bulk stream.</summary>
    [LoggerMessage(
        EventId = LogEventIds.EnqueuedToBulkStream,
        Level = LogLevel.Debug,
        Message = "Enqueued {LookupType} request to type-specific bulk stream {Stream}" )]
    private static partial void LogEnqueuedToBulkStream( ILogger logger, string lookupType, string stream );

    /// <summary>Logs that XAUTOCLAIM recovered pending entries.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AutoClaimRecovered,
        Level = LogLevel.Information,
        Message = "XAUTOCLAIM recovered {Count} pending entries from {Stream}" )]
    private static partial void LogAutoClaimRecovered( ILogger logger, int count, string stream );

    /// <summary>Logs that XAUTOCLAIM is not supported by the Redis server.</summary>
    [LoggerMessage(
        EventId = LogEventIds.AutoClaimNotSupported,
        Level = LogLevel.Warning,
        Message = "XAUTOCLAIM not supported on {Stream} — pending-entry recovery disabled (requires Redis ≥ 6.2)" )]
    private static partial void LogAutoClaimNotSupported( ILogger logger, Exception ex, string stream );

    /// <summary>Logs a deserialization error.</summary>
    [LoggerMessage(
        EventId = LogEventIds.DeserializationError,
        Level = LogLevel.Error,
        Message = "Failed to deserialize message {Id} from {Stream}" )]
    private static partial void LogDeserializationError( ILogger logger, Exception ex, string id, string stream );

    /// <summary>Logs a warning reading from a stream.</summary>
    [LoggerMessage(
        EventId = LogEventIds.StreamReadWarning,
        Level = LogLevel.Warning,
        Message = "Error reading from stream {Stream}" )]
    private static partial void LogStreamReadWarning( ILogger logger, Exception ex, string stream );

    /// <summary>Logs that a message was acknowledged.</summary>
    [LoggerMessage(
        EventId = LogEventIds.MessageAcknowledged,
        Level = LogLevel.Debug,
        Message = "Acknowledged and deleted message {MessageId} from {Stream}" )]
    private static partial void LogMessageAcknowledged( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message was not found for requeue.</summary>
    [LoggerMessage(
        EventId = LogEventIds.MessageNotFound,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} not found in {Stream} for requeue" )]
    private static partial void LogMessageNotFound( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message was requeued with an updated attempt count.</summary>
    [LoggerMessage(
        EventId = LogEventIds.MessageRequeued,
        Level = LogLevel.Debug,
        Message = "Requeued message from {Stream} (attempt {Attempt})" )]
    private static partial void LogMessageRequeued( ILogger logger, string stream, int attempt );

    /// <summary>Logs that a message exceeded max retry attempts.</summary>
    [LoggerMessage(
        EventId = LogEventIds.MaxRetriesExceeded,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} in bulk stream exceeded {MaxRetries} retry attempts; discarding (saga={SagaId})" )]
    private static partial void LogMaxRetriesExceeded( ILogger logger, string messageId, int maxRetries, string sagaId );

    #endregion
}

/// <summary>
/// Represents the depth of ID-based lookup requests in the Spotify bulk streams.
/// </summary>
/// <param name="TrackIdCount">Track ID lookups (equals <paramref name="BulkTrackIdCount"/>).</param>
/// <param name="AlbumIdCount">Album ID lookups (equals <paramref name="BulkAlbumIdCount"/>).</param>
/// <param name="BulkTrackIdCount">Track ID lookups in the bulk track-id stream.</param>
/// <param name="BulkAlbumIdCount">Album ID lookups in the bulk album-id stream.</param>
public sealed record IdLookupDepth(
    int TrackIdCount,
    int AlbumIdCount,
    int BulkTrackIdCount,
    int BulkAlbumIdCount
);

/// <summary>
/// Outcome returned by <see cref="SpotifyBatchQueueHelper.RequeueAsync"/>.
/// </summary>
public enum RequeueOutcome {
    /// <summary>The message was re-added to the stream for another attempt.</summary>
    Requeued,
    /// <summary>The retry cap was reached; the message was discarded. Caller must write failed saga state.</summary>
    CapReached,
    /// <summary>
    /// The stream entry was not found (already XDELed — duplicate in-flight after re-claim).
    /// Caller must leave the saga untouched.
    /// </summary>
    NotFound
}
