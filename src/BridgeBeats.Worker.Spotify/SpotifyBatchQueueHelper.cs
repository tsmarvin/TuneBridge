using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using StackExchange.Redis;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Spotify-specific batch queue helper that manages type-specific bulk streams
/// and provides cross-priority depth checking for opportunistic batching.
/// </summary>
/// <remarks>
/// <para>
/// This helper extends the standard queue infrastructure with Spotify-specific
/// optimizations for bulk lookups:
/// <list type="bullet">
///   <item>Type-specific bulk streams for track and album ID lookups</item>
///   <item>Cross-priority depth checking to batch when thresholds are met</item>
///   <item>Batch dequeue for efficient bulk API utilization</item>
/// </list>
/// </para>
/// <para>
/// Stream naming convention:
/// <list type="bullet">
///   <item><c>queue:spotify:bulk:track-id</c> - SongIdLookup bulk messages</item>
///   <item><c>queue:spotify:bulk:album-id</c> - AlbumIdLookup bulk messages</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SpotifyBatchQueueHelper {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SpotifyBatchQueueHelper> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // Type-specific bulk streams
    private const string BulkTrackIdStream = "queue:spotify:bulk:track-id";
    private const string BulkAlbumIdStream = "queue:spotify:bulk:album-id";

    // Standard priority streams for depth checking
    private const string InteractiveStream = "queue:spotify:interactive";
    private const string BackgroundStream = "queue:spotify:background";
    private const string BulkStream = "queue:spotify:bulk";

    private const string ConsumerGroup = "spotify-workers";
    private readonly string _consumerId;

    private const string MessagePayloadField = "payload";
    private const string MessageEnqueuedAtField = "enqueuedAt";

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

        foreach (string stream in new[] { BulkTrackIdStream, BulkAlbumIdStream }) {
            try {
                _ = await db.StreamCreateConsumerGroupAsync(
                    stream,
                    ConsumerGroup,
                    StreamPosition.NewMessages,
                    createStream: true
                );
                _logger.LogInformation(
                    "Created consumer group {Group} for stream {Stream}",
                    ConsumerGroup,
                    stream
                );
            } catch (RedisServerException ex) when (ex.Message.Contains( "BUSYGROUP" )) {
                _logger.LogDebug(
                    "Consumer group {Group} already exists for stream {Stream}",
                    ConsumerGroup,
                    stream
                );
            }
        }
    }

    /// <summary>
    /// Gets the current depth of ID-based lookup requests across all priority queues.
    /// </summary>
    /// <returns>A record containing track and album ID lookup counts.</returns>
    public async Task<IdLookupDepth> GetIdLookupDepthAsync( CancellationToken cancellationToken = default ) {
        IDatabase db = _redis.GetDatabase( );

        // Get counts from type-specific bulk streams
        long bulkTrackCount = await db.StreamLengthAsync( BulkTrackIdStream );
        long bulkAlbumCount = await db.StreamLengthAsync( BulkAlbumIdStream );



        // Also scan standard priority streams for ID-based lookups
        int interactiveTrackCount;
        int interactiveAlbumCount;
        // Scan interactive stream
        (interactiveTrackCount, interactiveAlbumCount) = await CountIdLookupsInStreamAsync( db, InteractiveStream );
        int backgroundTrackCount;
        int backgroundAlbumCount;
        // Scan background stream
        (backgroundTrackCount, backgroundAlbumCount) = await CountIdLookupsInStreamAsync( db, BackgroundStream );

        return new IdLookupDepth(
            TrackIdCount: (int)bulkTrackCount + interactiveTrackCount + backgroundTrackCount,
            AlbumIdCount: (int)bulkAlbumCount + interactiveAlbumCount + backgroundAlbumCount,
            BulkTrackIdCount: (int)bulkTrackCount,
            BulkAlbumIdCount: (int)bulkAlbumCount
        );
    }

    /// <summary>
    /// Counts ID-based lookup requests in a standard priority stream.
    /// </summary>
    private async Task<(int trackCount, int albumCount)> CountIdLookupsInStreamAsync( IDatabase db, string stream ) {
        int trackCount = 0;
        int albumCount = 0;

        try {
            StreamEntry[] entries = await db.StreamRangeAsync( stream, "-", "+", count: 100 );

            foreach (StreamEntry entry in entries) {
                string? payload = entry[MessagePayloadField];
                if (string.IsNullOrEmpty( payload )) { continue; }

                try {
                    QueuedLookupRequest? request = JsonSerializer.Deserialize<QueuedLookupRequest>( payload, _jsonOptions );
                    if (request is null) { continue; }

                    if (request.LookupType == LookupRequestType.SongIdLookup) {
                        trackCount++;
                    } else if (request.LookupType == LookupRequestType.AlbumIdLookup) {
                        albumCount++;
                    }
                } catch (JsonException) {
                    // Skip malformed messages
                }
            }
        } catch (RedisServerException) {
            // Stream may not exist yet
        }

        return (trackCount, albumCount);
    }

    /// <summary>
    /// Determines if track ID lookups should be batched based on current queue depth.
    /// </summary>
    /// <returns>True if the threshold for bulk track lookups is met.</returns>
    public async Task<bool> ShouldBatchTrackLookupsAsync( CancellationToken cancellationToken = default ) {
        IdLookupDepth depth = await GetIdLookupDepthAsync( cancellationToken );
        return depth.TrackIdCount >= SpotifyConstants.MaxTracksPerBatchLookup;
    }

    /// <summary>
    /// Determines if album ID lookups should be batched based on current queue depth.
    /// </summary>
    /// <returns>True if the threshold for bulk album lookups is met.</returns>
    public async Task<bool> ShouldBatchAlbumLookupsAsync( CancellationToken cancellationToken = default ) {
        IdLookupDepth depth = await GetIdLookupDepthAsync( cancellationToken );
        return depth.AlbumIdCount >= SpotifyConstants.MaxAlbumsPerBatchLookup;
    }

    /// <summary>
    /// Enqueues a lookup request to the appropriate type-specific bulk stream.
    /// </summary>
    /// <param name="request">The lookup request to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the request was enqueued to a type-specific stream; false if not applicable.</returns>
    public async Task<bool> TryEnqueueToBulkTypeStreamAsync(
        QueuedLookupRequest request,
        CancellationToken cancellationToken = default
    ) {
        string? stream = request.LookupType switch {
            LookupRequestType.SongIdLookup => BulkTrackIdStream,
            LookupRequestType.AlbumIdLookup => BulkAlbumIdStream,
            _ => null
        };

        if (stream is null) {
            return false;
        }

        IDatabase db = _redis.GetDatabase( );
        string payload = JsonSerializer.Serialize( request, _jsonOptions );

        NameValueEntry[] fields = [
            new NameValueEntry( MessagePayloadField, payload ),
            new NameValueEntry( MessageEnqueuedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( stream, fields );

        _logger.LogDebug(
            "Enqueued {LookupType} request to type-specific bulk stream {Stream}",
            request.LookupType,
            stream
        );

        return true;
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
        return await DequeueBatchFromStreamAsync( BulkTrackIdStream, count, cancellationToken );
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
        return await DequeueBatchFromStreamAsync( BulkAlbumIdStream, count, cancellationToken );
    }

    /// <summary>
    /// Dequeues ID-based lookup requests from standard priority streams for opportunistic batching.
    /// </summary>
    /// <param name="lookupType">The type of lookup to collect (SongIdLookup or AlbumIdLookup).</param>
    /// <param name="maxCount">Maximum number of requests to collect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of queued messages collected from interactive and background streams.</returns>
    public async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> CollectIdLookupsFromPriorityStreamsAsync(
        LookupRequestType lookupType,
        int maxCount,
        CancellationToken cancellationToken = default
    ) {
        List<QueuedMessage<QueuedLookupRequest>> collected = [];
        IDatabase db = _redis.GetDatabase( );

        // Collect from interactive stream first, then background
        foreach (string stream in new[] { InteractiveStream, BackgroundStream }) {
            if (collected.Count >= maxCount) { break; }

            int remaining = maxCount - collected.Count;
            IReadOnlyList<QueuedMessage<QueuedLookupRequest>> fromStream =
                await CollectIdLookupsFromStreamAsync( db, stream, lookupType, remaining, cancellationToken );

            collected.AddRange( fromStream );
        }

        return collected;
    }

    /// <summary>
    /// Collects ID-based lookup requests from a specific stream.
    /// </summary>
    private async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> CollectIdLookupsFromStreamAsync(
        IDatabase db,
        string stream,
        LookupRequestType targetType,
        int maxCount,
        CancellationToken cancellationToken
    ) {
        List<QueuedMessage<QueuedLookupRequest>> collected = [];

        try {
            // Peek at entries in the stream
            StreamEntry[] entries = await db.StreamRangeAsync( stream, "-", "+", count: maxCount * 2 );

            foreach (StreamEntry entry in entries) {
                if (collected.Count >= maxCount) { break; }

                string? payload = entry[MessagePayloadField];
                if (string.IsNullOrEmpty( payload )) { continue; }

                try {
                    QueuedLookupRequest? request = JsonSerializer.Deserialize<QueuedLookupRequest>( payload, _jsonOptions );
                    if (request is null || request.LookupType != targetType) { continue; }

                    // Claim the message
                    StreamEntry[] claimed = await db.StreamClaimAsync(
                        stream,
                        ConsumerGroup,
                        _consumerId,
                        minIdleTimeInMs: 0,
                        messageIds: [entry.Id]
                    );

                    if (claimed.Length > 0) {
                        string? enqueuedAtStr = entry[MessageEnqueuedAtField];
                        DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                            ? DateTimeOffset.Parse( enqueuedAtStr )
                            : DateTimeOffset.UtcNow;

                        string compositeId = $"{stream}:{entry.Id}";
                        collected.Add( new QueuedMessage<QueuedLookupRequest>( compositeId, request, enqueuedAt ) );
                    }
                } catch (JsonException) {
                    // Skip malformed messages
                }
            }
        } catch (RedisServerException ex) {
            _logger.LogWarning( ex, "Error collecting ID lookups from stream {Stream}", stream );
        }

        return collected;
    }

    /// <summary>
    /// Dequeues a batch of messages from a specific stream.
    /// </summary>
    private async Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> DequeueBatchFromStreamAsync(
        string stream,
        int count,
        CancellationToken cancellationToken
    ) {
        IDatabase db = _redis.GetDatabase( );
        List<QueuedMessage<QueuedLookupRequest>> messages = [];

        try {
            StreamEntry[] entries = await db.StreamReadGroupAsync(
                stream,
                ConsumerGroup,
                _consumerId,
                count: count,
                noAck: false
            );

            foreach (StreamEntry entry in entries) {
                string? payload = entry[MessagePayloadField];
                string? enqueuedAtStr = entry[MessageEnqueuedAtField];

                if (string.IsNullOrEmpty( payload )) { continue; }

                try {
                    QueuedLookupRequest? request = JsonSerializer.Deserialize<QueuedLookupRequest>( payload, _jsonOptions );
                    if (request is null) { continue; }

                    DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                        ? DateTimeOffset.Parse( enqueuedAtStr )
                        : DateTimeOffset.UtcNow;

                    string compositeId = $"{stream}:{entry.Id}";
                    messages.Add( new QueuedMessage<QueuedLookupRequest>( compositeId, request, enqueuedAt ) );
                } catch (JsonException ex) {
                    _logger.LogError( ex, "Failed to deserialize message {Id} from {Stream}", entry.Id, stream );
                }
            }
        } catch (RedisServerException ex) {
            _logger.LogWarning( ex, "Error reading from stream {Stream}", stream );
        }

        return messages;
    }

    /// <summary>
    /// Acknowledges and removes a message from its stream.
    /// </summary>
    /// <param name="messageId">The composite message ID (stream:id format).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AcknowledgeAsync( string messageId, CancellationToken cancellationToken = default ) {
        (string stream, string id) = ParseMessageId( messageId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        _logger.LogDebug( "Acknowledged and deleted message {MessageId} from {Stream}", id, stream );
    }

    /// <summary>
    /// Requeues a message to its original stream.
    /// </summary>
    /// <param name="messageId">The composite message ID (stream:id format).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RequeueAsync( string messageId, CancellationToken cancellationToken = default ) {
        (string stream, string id) = ParseMessageId( messageId );

        IDatabase db = _redis.GetDatabase( );

        // Read the original message
        StreamEntry[] entries = await db.StreamRangeAsync( stream, id, id, count: 1 );

        if (entries.Length == 0) {
            _logger.LogWarning( "Message {MessageId} not found in {Stream} for requeue", id, stream );
            return;
        }

        StreamEntry original = entries[0];

        // Acknowledge and delete the original
        _ = await db.StreamAcknowledgeAsync( stream, ConsumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        // Re-add with updated enqueued time
        NameValueEntry[] fields = [
            new NameValueEntry( MessagePayloadField, original[MessagePayloadField] ),
            new NameValueEntry( MessageEnqueuedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( stream, fields );

        _logger.LogDebug( "Requeued message from {Stream}", stream );
    }

    private static (string stream, string id) ParseMessageId( string compositeId ) {
        // Format: stream:id where id may contain colons (Redis stream IDs are timestamp-sequence)
        // Find the last occurrence of the stream name pattern
        foreach (string streamPrefix in new[] { BulkTrackIdStream, BulkAlbumIdStream, InteractiveStream, BackgroundStream, BulkStream }) {
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
}

/// <summary>
/// Represents the depth of ID-based lookup requests in the queue system.
/// </summary>
/// <param name="TrackIdCount">Total track ID lookups across all priorities.</param>
/// <param name="AlbumIdCount">Total album ID lookups across all priorities.</param>
/// <param name="BulkTrackIdCount">Track ID lookups in the bulk type-specific stream.</param>
/// <param name="BulkAlbumIdCount">Album ID lookups in the bulk type-specific stream.</param>
public sealed record IdLookupDepth(
    int TrackIdCount,
    int AlbumIdCount,
    int BulkTrackIdCount,
    int BulkAlbumIdCount
);
