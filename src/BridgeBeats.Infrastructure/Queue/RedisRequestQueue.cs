using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Queue;

/// <summary>
/// Redis Streams-based implementation of <see cref="IRequestQueue{T}"/> for a specific music provider.
/// </summary>
/// <remarks>
/// <para>
/// Uses Redis Streams with consumer groups for reliable message delivery.
/// Each provider has three priority streams (interactive, background, bulk)
/// and a Dead Letter Queue for failed messages.
/// </para>
/// <para>
/// Stream naming convention:
/// <list type="bullet">
///   <item><c>queue:{provider}:interactive</c> - Interactive priority stream</item>
///   <item><c>queue:{provider}:background</c> - Background priority stream</item>
///   <item><c>queue:{provider}:bulk</c> - Bulk priority stream</item>
///   <item><c>queue:{provider}:dlq</c> - Dead Letter Queue</item>
/// </list>
/// </para>
/// </remarks>
/// <typeparam name="T">The type of request to queue.</typeparam>
public sealed class RedisRequestQueue<T> : IRequestQueue<T> where T : class, IQueueableRequest {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRequestQueue<T>> _logger;
    private readonly QueueSettings _settings;
    private readonly SupportedProviders _provider;
    private readonly string _consumerGroup;
    private readonly string _consumerId;
    private readonly JsonSerializerOptions _jsonOptions;

    // Stream keys
    private readonly string _interactiveStream;
    private readonly string _backgroundStream;
    private readonly string _bulkStream;
    private readonly string _dlqStream;

    // Track which stream a message came from for ack/requeue
    private const string MessagePayloadField = "payload";
    private const string MessageEnqueuedAtField = "enqueuedAt";
    private const string DlqReasonField = "dlqReason";
    private const string DlqMovedAtField = "dlqMovedAt";
    private const string DlqOriginalStreamField = "originalStream";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisRequestQueue{T}"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="settings">Queue configuration settings.</param>
    /// <param name="provider">The provider this queue serves.</param>
    public RedisRequestQueue(
        IConnectionMultiplexer redis,
        ILogger<RedisRequestQueue<T>> logger,
        IOptions<QueueSettings> settings,
        SupportedProviders provider
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _settings = settings?.Value ?? throw new ArgumentNullException( nameof( settings ) );
        _provider = provider;

        string providerName = provider.ToString( ).ToLowerInvariant( );
        _interactiveStream = $"queue:{providerName}:interactive";
        _backgroundStream = $"queue:{providerName}:background";
        _bulkStream = $"queue:{providerName}:bulk";
        _dlqStream = $"queue:{providerName}:dlq";

        _consumerGroup = $"{providerName}-workers";
        _consumerId = $"{providerName}-worker-{Guid.NewGuid( ):N}";

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Ensures consumer groups exist for all priority streams.
    /// Call this during worker startup.
    /// </summary>
    public async Task EnsureConsumerGroupsAsync( CancellationToken cancellationToken = default ) {
        IDatabase db = _redis.GetDatabase( );

        foreach (string stream in new[] { _interactiveStream, _backgroundStream, _bulkStream }) {
            try {
                // XGROUP CREATE creates the stream if it doesn't exist
                _ = await db.StreamCreateConsumerGroupAsync(
                    stream,
                    _consumerGroup,
                    StreamPosition.NewMessages,
                    createStream: true
                );
                _logger.LogInformation(
                    "Created consumer group {Group} for stream {Stream}",
                    _consumerGroup,
                    stream
                );
            } catch (RedisServerException ex) when (ex.Message.Contains( "BUSYGROUP" )) {
                // Consumer group already exists, which is fine
                _logger.LogDebug(
                    "Consumer group {Group} already exists for stream {Stream}",
                    _consumerGroup,
                    stream
                );
            }
        }
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync( T request, QueuePriority priority, CancellationToken cancellationToken = default ) {
        ArgumentNullException.ThrowIfNull( request );

        string stream = GetStreamForPriority( priority );
        string payload = JsonSerializer.Serialize( request, _jsonOptions );

        IDatabase db = _redis.GetDatabase( );
        NameValueEntry[] fields = [
            new NameValueEntry( MessagePayloadField, payload ),
            new NameValueEntry( MessageEnqueuedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        RedisValue messageId = await db.StreamAddAsync( stream, fields );

        // Record enqueue metric
        QueueMetrics.RecordEnqueue( _provider, priority );

        _logger.LogDebug(
            "Enqueued message {MessageId} to {Stream} with priority {Priority}",
            messageId,
            stream,
            priority
        );
    }

    /// <inheritdoc/>
    public async Task<QueuedMessage<T>?> DequeueAsync( CancellationToken cancellationToken = default ) {
        IDatabase db = _redis.GetDatabase( );

        // Use weighted random selection to choose which stream to read from
        // with bulk gating until minimum queue depth is reached
        string[] streamOrder = await GetWeightedStreamOrderWithBulkGatingAsync( );

        foreach (string stream in streamOrder) {
            StreamEntry[] entries = await db.StreamReadGroupAsync(
                stream,
                _consumerGroup,
                _consumerId,
                count: 1,
                noAck: false
            );

            if (entries.Length > 0) {
                StreamEntry entry = entries[0];
                QueuedMessage<T>? message = ParseStreamEntry( entry, stream );
                if (message is not null) {
                    // Record dequeue metric based on stream priority
                    QueuePriority priority = GetPriorityFromStream( stream );
                    QueueMetrics.RecordDequeue( _provider, priority );
                }
                return message;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<QueuedMessage<T>?> DequeueAsync( IRateLimitTracker rateLimitTracker, CancellationToken cancellationToken = default ) {
        ArgumentNullException.ThrowIfNull( rateLimitTracker );

        IDatabase db = _redis.GetDatabase( );

        // Get all currently rate-limited endpoints for this provider
        IReadOnlyList<RateLimitedEndpoint> rateLimitedEndpoints = await rateLimitTracker.GetAllRateLimitedAsync( _provider, cancellationToken );
        HashSet<string> blockedEndpoints = rateLimitedEndpoints.Select( e => e.Endpoint ).ToHashSet( StringComparer.OrdinalIgnoreCase );

        // Use weighted random selection to choose which stream to read from
        // with bulk gating until minimum queue depth is reached
        string[] streamOrder = await GetWeightedStreamOrderWithBulkGatingAsync( );

        foreach (string stream in streamOrder) {
            // Peek at pending messages to find one that's not rate-limited
            QueuedMessage<T>? eligibleMessage = await FindEligibleMessageAsync( db, stream, blockedEndpoints, cancellationToken );
            if (eligibleMessage is not null) {
                // Record dequeue metric based on stream priority
                QueuePriority priority = GetPriorityFromStream( stream );
                QueueMetrics.RecordDequeue( _provider, priority );
                return eligibleMessage;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first eligible message in the stream that is not rate-limited.
    /// Scans pending messages without claiming them, then claims the first eligible one.
    /// </summary>
    private async Task<QueuedMessage<T>?> FindEligibleMessageAsync(
        IDatabase db,
        string stream,
        HashSet<string> blockedEndpoints,
        CancellationToken cancellationToken
    ) {
        // First, check for any pending messages already claimed by this consumer
        // that may need to be processed (recovery scenario)
        StreamPendingMessageInfo[]? pendingMessages = null;
        try {
            pendingMessages = await db.StreamPendingMessagesAsync(
                stream,
                _consumerGroup,
                count: 50, // Check a reasonable batch
                _consumerId
            );
        } catch (RedisServerException) {
            // Consumer group may not exist yet or no pending messages
        }

        // Process pending messages first (already claimed)
        if (pendingMessages is { Length: > 0 }) {
            foreach (StreamPendingMessageInfo pending in pendingMessages) {
                // Read the message content
                StreamEntry[] entries = await db.StreamRangeAsync( stream, pending.MessageId, pending.MessageId, count: 1 );
                if (entries.Length == 0) { continue; }

                StreamEntry entry = entries[0];
                QueuedMessage<T>? message = ParseStreamEntry( entry, stream );
                if (message is null) { continue; }

                // Check if this message's endpoint is blocked
                if (!IsMessageBlocked( message.Payload, blockedEndpoints )) {
                    return message;
                }

                // Message is blocked - leave it pending, don't acknowledge
                _logger.LogDebug(
                    "Skipping rate-limited message {MessageId} in {Stream} - endpoint is blocked",
                    pending.MessageId,
                    stream
                );
            }
        }

        // No eligible pending messages, try to read new messages
        // Use XRANGE to peek at the stream without claiming
        StreamEntry[] newEntries = await db.StreamRangeAsync(
            stream,
            "-", // Start from beginning
            "+", // To end
            count: 50 // Reasonable batch to scan
        );

        foreach (StreamEntry entry in newEntries) {
            QueuedMessage<T>? parsed = ParseStreamEntryForPeek( entry, stream );
            if (parsed is null) { continue; }

            // Check if this message's endpoint is blocked
            if (IsMessageBlocked( parsed.Payload, blockedEndpoints )) {
                _logger.LogDebug(
                    "Skipping rate-limited message {MessageId} in {Stream}",
                    entry.Id,
                    stream
                );
                continue;
            }

            // Found an eligible message - claim it with XREADGROUP using specific ID
            // We need to use XCLAIM to claim a specific message
            StreamEntry[] claimed = await db.StreamClaimAsync(
                stream,
                _consumerGroup,
                _consumerId,
                minIdleTimeInMs: 0, // Claim immediately regardless of idle time
                messageIds: [entry.Id]
            );

            if (claimed.Length > 0) {
                return ParseStreamEntry( claimed[0], stream );
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a stream entry for peeking (without the composite ID format used for claimed messages).
    /// </summary>
    private QueuedMessage<T>? ParseStreamEntryForPeek( StreamEntry entry, string stream ) {
        string? payload = entry[MessagePayloadField];
        string? enqueuedAtStr = entry[MessageEnqueuedAtField];

        if (string.IsNullOrEmpty( payload )) {
            return null;
        }

        try {
            T? request = JsonSerializer.Deserialize<T>( payload, _jsonOptions );
            if (request is null) { return null; }

            DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                ? DateTimeOffset.Parse( enqueuedAtStr )
                : DateTimeOffset.UtcNow;

            // Composite message ID includes stream for ack/requeue
            string compositeId = $"{stream}:{entry.Id}";
            return new QueuedMessage<T>( compositeId, request, enqueuedAt );
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Determines if a message should be blocked based on its lookup type.
    /// </summary>
    private bool IsMessageBlocked( T request, HashSet<string> blockedEndpoints ) {
        // Extract the lookup type from the request if it's a QueuedLookupRequest
        if (request is QueuedLookupRequest lookupRequest) {
            // Use the LookupType as the endpoint key for rate limiting
            string endpointKey = lookupRequest.LookupType.ToString( );
            return blockedEndpoints.Contains( endpointKey );
        }

        // For other request types, don't block
        return false;
    }

    /// <inheritdoc/>
    public async Task AcknowledgeAsync( string messageId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

        (string stream, string id) = ParseMessageId( messageId );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.StreamAcknowledgeAsync( stream, _consumerGroup, id );

        // Also delete the message from the stream (cleanup)
        _ = await db.StreamDeleteAsync( stream, [id] );

        // Record acknowledge metric
        QueueMetrics.RecordAcknowledge( _provider );

        _logger.LogDebug( "Acknowledged and deleted message {MessageId} from {Stream}", id, stream );
    }

    /// <inheritdoc/>
    public async Task RequeueAsync( string messageId, TimeSpan? delay = null, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

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
        _ = await db.StreamAcknowledgeAsync( stream, _consumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        if (delay.HasValue && delay.Value > TimeSpan.Zero) {
            // For delayed requeue, we'd need a separate delay mechanism (e.g., sorted set with score = delivery time)
            // For now, add immediately with a note. Phase 4 workers can implement delay checking.
            _logger.LogDebug( "Delayed requeue requested but not yet implemented. Adding immediately." );
        }

        // Re-add with updated enqueued time
        NameValueEntry[] fields = [
            new NameValueEntry( MessagePayloadField, original[MessagePayloadField] ),
            new NameValueEntry( MessageEnqueuedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( stream, fields );

        // Record requeue metric
        QueueMetrics.RecordRequeue( _provider );

        _logger.LogDebug( "Requeued message from {Stream}", stream );
    }

    /// <inheritdoc/>
    public async Task<QueueDepth> GetDepthAsync( CancellationToken cancellationToken = default ) {
        IDatabase db = _redis.GetDatabase( );

        long interactive = await db.StreamLengthAsync( _interactiveStream );
        long background = await db.StreamLengthAsync( _backgroundStream );
        long bulk = await db.StreamLengthAsync( _bulkStream );

        return new QueueDepth(
            Interactive: (int)interactive,
            Background: (int)background,
            Bulk: (int)bulk,
            Total: (int)(interactive + background + bulk)
        );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedMessage<T>>> GetDlqMessagesAsync( int limit, CancellationToken cancellationToken = default ) {
        if (limit <= 0) { limit = 100; }

        IDatabase db = _redis.GetDatabase( );
        StreamEntry[] entries = await db.StreamRangeAsync( _dlqStream, count: limit );

        List<QueuedMessage<T>> messages = new( entries.Length );
        foreach (StreamEntry entry in entries) {
            QueuedMessage<T>? parsed = ParseStreamEntry( entry, _dlqStream );
            if (parsed is not null) {
                messages.Add( parsed );
            }
        }

        return messages;
    }

    /// <inheritdoc/>
    public async Task RequeueFromDlqAsync( string messageId, QueuePriority priority, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

        (string stream, string id) = ParseMessageId( messageId );

        if (stream != _dlqStream) {
            throw new ArgumentException( $"Message {messageId} is not from the DLQ", nameof( messageId ) );
        }

        IDatabase db = _redis.GetDatabase( );
        StreamEntry[] entries = await db.StreamRangeAsync( _dlqStream, id, id, count: 1 );

        if (entries.Length == 0) {
            throw new InvalidOperationException( $"Message {id} not found in DLQ" );
        }

        StreamEntry original = entries[0];
        string targetStream = GetStreamForPriority( priority );

        // Add to target stream
        NameValueEntry[] fields = [
            new NameValueEntry( MessagePayloadField, original[MessagePayloadField] ),
            new NameValueEntry( MessageEnqueuedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( targetStream, fields );

        // Delete from DLQ
        _ = await db.StreamDeleteAsync( _dlqStream, [id] );

        _logger.LogInformation(
            "Moved message {MessageId} from DLQ to {Stream} with priority {Priority}",
            id,
            targetStream,
            priority
        );
    }

    /// <inheritdoc/>
    public async Task MoveToDlqAsync( string messageId, string reason, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

        (string stream, string id) = ParseMessageId( messageId );
        IDatabase db = _redis.GetDatabase( );

        // Read the original message
        StreamEntry[] entries = await db.StreamRangeAsync( stream, id, id, count: 1 );

        if (entries.Length == 0) {
            _logger.LogWarning( "Message {MessageId} not found in {Stream} for DLQ move", id, stream );
            return;
        }

        StreamEntry original = entries[0];

        // Add to DLQ with metadata
        NameValueEntry[] dlqFields = [
            new NameValueEntry( MessagePayloadField, original[MessagePayloadField] ),
            new NameValueEntry( MessageEnqueuedAtField, original[MessageEnqueuedAtField] ),
            new NameValueEntry( DlqOriginalStreamField, stream ),
            new NameValueEntry( DlqReasonField, reason ),
            new NameValueEntry( DlqMovedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( _dlqStream, dlqFields );

        // Acknowledge and delete from original stream
        _ = await db.StreamAcknowledgeAsync( stream, _consumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        _logger.LogWarning(
            "Moved message {MessageId} from {Stream} to DLQ. Reason: {Reason}",
            id,
            stream,
            reason
        );
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteFromDlqAsync( string messageId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

        (string stream, string id) = ParseMessageId( messageId );

        if (stream != _dlqStream) {
            return false;
        }

        IDatabase db = _redis.GetDatabase( );
        long deleted = await db.StreamDeleteAsync( _dlqStream, [id] );

        if (deleted > 0) {
            _logger.LogInformation( "Deleted message {MessageId} from DLQ", id );
        }

        return deleted > 0;
    }

    private string GetStreamForPriority( QueuePriority priority ) => priority switch {
        QueuePriority.Interactive => _interactiveStream,
        QueuePriority.Background => _backgroundStream,
        QueuePriority.Bulk => _bulkStream,
        _ => throw new ArgumentOutOfRangeException( nameof( priority ) )
    };

    private QueuePriority GetPriorityFromStream( string stream ) {
        if (stream == _interactiveStream) { return QueuePriority.Interactive; }
        if (stream == _backgroundStream) { return QueuePriority.Background; }
        if (stream == _bulkStream) { return QueuePriority.Bulk; }
        return QueuePriority.Background; // Default fallback
    }

    private string[] GetWeightedStreamOrder( ) {
        // Weighted random selection based on priority weights
        // Returns streams in priority order based on weighted random selection
        int total = _settings.Weights.Interactive + _settings.Weights.Background + _settings.Weights.Bulk;
        int roll = Random.Shared.Next( total );

        return roll < _settings.Weights.Interactive
            ? [_interactiveStream, _backgroundStream, _bulkStream]
            : roll < _settings.Weights.Interactive + _settings.Weights.Background
            ? [_backgroundStream, _interactiveStream, _bulkStream]
            : [_bulkStream, _interactiveStream, _backgroundStream];
    }

    private async Task<string[]> GetWeightedStreamOrderWithBulkGatingAsync( ) {
        // Gate bulk processing until there are enough messages to batch
        int minBulkThreshold = _settings.GetMinBulkThreshold( _provider );

        if (minBulkThreshold > 0) {
            QueueDepth depth = await GetDepthAsync( );

            if (depth.Bulk < minBulkThreshold) {
                // Not enough bulk messages to process - skip bulk queue
                _logger.LogDebug(
                    "Gating bulk operations - queue depth ({Depth}) below threshold ({Threshold})",
                    depth.Bulk,
                    minBulkThreshold
                );

                // Return order without bulk
                int total = _settings.Weights.Interactive + _settings.Weights.Background;
                int roll = Random.Shared.Next( total );

                return roll < _settings.Weights.Interactive
                    ? [_interactiveStream, _backgroundStream]
                    : [_backgroundStream, _interactiveStream];
            }
        }

        // Normal weighted selection including bulk
        return GetWeightedStreamOrder( );
    }

    private QueuedMessage<T>? ParseStreamEntry( StreamEntry entry, string stream ) {
        string? payload = entry[MessagePayloadField];
        string? enqueuedAtStr = entry[MessageEnqueuedAtField];

        if (string.IsNullOrEmpty( payload )) {
            _logger.LogWarning( "Message {Id} in {Stream} has no payload", entry.Id, stream );
            return null;
        }

        try {
            T? request = JsonSerializer.Deserialize<T>( payload, _jsonOptions );
            if (request is null) {
                _logger.LogWarning( "Failed to deserialize message {Id} payload", entry.Id );
                return null;
            }

            DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                ? DateTimeOffset.Parse( enqueuedAtStr )
                : DateTimeOffset.UtcNow;

            // Composite message ID includes stream for ack/requeue
            string compositeId = $"{stream}:{entry.Id}";

            return new QueuedMessage<T>( compositeId, request, enqueuedAt );
        } catch (JsonException ex) {
            _logger.LogError( ex, "Failed to deserialize message {Id} from {Stream}", entry.Id, stream );
            return null;
        }
    }

    private static (string stream, string id) ParseMessageId( string compositeId ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( compositeId );

        // Redis stream IDs are in format "timestamp-sequence" (e.g., "1234567890123-0")
        // Our composite format is "stream:timestamp-sequence"
        // We need to find the last occurrence of a pattern that looks like a Redis stream ID

        // Find the last dash which should be part of the Redis stream ID
        int lastDash = compositeId.LastIndexOf( '-' );
        if (lastDash <= 0) {
            throw new ArgumentException( $"Invalid composite message ID (no dash found): {compositeId}", nameof( compositeId ) );
        }

        // Find the last colon before the timestamp part of the stream ID
        // Work backwards from the dash to find where timestamp begins
        int searchStart = lastDash - 1;
        while (searchStart > 0 && char.IsDigit( compositeId[searchStart] )) {
            searchStart--;
        }

        // Validate we found a proper separator and have a complete timestamp
        if (searchStart < 0 || compositeId[searchStart] != ':') {
            throw new ArgumentException( $"Invalid composite message ID format: {compositeId}", nameof( compositeId ) );
        }

        // Verify we have at least one digit in the timestamp portion
        if (searchStart >= lastDash - 1) {
            throw new ArgumentException( $"Invalid composite message ID: no timestamp digits found: {compositeId}", nameof( compositeId ) );
        }

        // Split at this colon: everything before is stream, everything after is the Redis ID
        string stream = compositeId[..searchStart];
        string id = compositeId[(searchStart + 1)..];

        return (stream, id);
    }
}
