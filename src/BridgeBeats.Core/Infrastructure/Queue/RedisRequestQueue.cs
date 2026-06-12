using System.Text.Json;
using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Helper class containing shared regex patterns for Redis queue operations.
/// Separate from generic <see cref="RedisRequestQueue{T}"/> to avoid static field in generic type warning.
/// </summary>
internal static partial class RedisQueuePatterns {
    /// <summary>
    /// Regex pattern to match Redis stream ID format (timestamp-sequence) at the end of a composite ID.
    /// Stream name must contain at least one character (pattern matches everything before last colon-delimited Redis ID).
    /// </summary>
    internal static readonly Regex RedisStreamIdPattern = GenerateRedisStreamIdPattern( );

    [GeneratedRegex( @"^(.+):(\d+-\d+)$" )]
    private static partial Regex GenerateRedisStreamIdPattern( );
}

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
public sealed partial class RedisRequestQueue<T> : IRequestQueue<T> where T : class, IQueueableRequest {

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

    // Effective aging interval (validated in constructor; N ≤ 1 falls back to default)
    private readonly int _agingInterval;

    // Per-instance dequeue ordering counter — single consumer per queue instance, no volatile required.
    // Unchecked increment: wraps at int.MaxValue harmlessly (the modulo arithmetic continues to work).
    private int _dequeueCounter;

    // Track which stream a message came from for ack/requeue
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
        ArgumentNullException.ThrowIfNull( settings );
        _settings = settings.Value;
        _provider = provider;

        // Validate the aging interval. N ≤ 1 is misconfiguration: N=1 would demote on every
        // single dequeue, silently inverting interactive priority on a config typo.
        // Fall back to the default (8) and log a warning at first use.
        const int DefaultAgingInterval = 8;
        int configuredInterval = _settings.InteractiveAgingInterval;
        if (configuredInterval <= 1) {
            _agingInterval = DefaultAgingInterval;
            LogAgingIntervalMisconfigured( _logger, configuredInterval, DefaultAgingInterval );
        } else {
            _agingInterval = configuredInterval;
        }

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
            if (cancellationToken.IsCancellationRequested) { break; }
            try {
                // XGROUP CREATE creates the stream if it doesn't exist
                _ = await db.StreamCreateConsumerGroupAsync(
                    stream,
                    _consumerGroup,
                    StreamPosition.NewMessages,
                    createStream: true
                );
                LogConsumerGroupCreated( _logger, _consumerGroup, stream );
            } catch (RedisServerException ex) when (ex.Message.Contains( "BUSYGROUP" )) {
                // Consumer group already exists, which is fine
                LogConsumerGroupExists( _logger, _consumerGroup, stream );
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
            new NameValueEntry( QueueStreamFieldNames.Payload, payload ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        RedisValue messageId = await db.StreamAddAsync( stream, fields );

        // Record enqueue metric
        QueueMetrics.RecordEnqueue( _provider, priority );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            string msgId = messageId!;
            LogEnqueued( _logger, msgId, stream, priority );
        }
    }

    /// <inheritdoc/>
    public async Task<QueuedMessage<T>?> DequeueAsync( CancellationToken cancellationToken = default ) {
        IDatabase db = _redis.GetDatabase( );

        // Determine stream order: interactive-first with aging and bulk gating.
        unchecked { _dequeueCounter++; }
        string[] streamOrder = await GetWeightedStreamOrderWithBulkGatingAsync( _dequeueCounter );

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

        // Determine stream order: interactive-first with aging and bulk gating.
        unchecked { _dequeueCounter++; }
        string[] streamOrder = await GetWeightedStreamOrderWithBulkGatingAsync( _dequeueCounter );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            LogDequeueStarting( _logger, blockedEndpoints.Count, streamOrder.Length );
        }

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

        if (_logger.IsEnabled( LogLevel.Debug )) {
            LogDequeueNoEligibleMessages( _logger, streamOrder.Length );
        }

        return null;
    }

    /// <summary>
    /// Finds the first eligible message in the stream that is not rate-limited.
    /// First processes pending messages (recovery scenario), then reads new messages via XREADGROUP.
    /// </summary>
    private async Task<QueuedMessage<T>?> FindEligibleMessageAsync(
        IDatabase db,
        string stream,
        HashSet<string> blockedEndpoints,
        CancellationToken cancellationToken
    ) {
        _ = cancellationToken; // Reserved for future async cancellation support
        int pendingCount = 0;
        int blockedPendingCount = 0;
        int newMessagesRead = 0;
        int blockedNewCount = 0;

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
            pendingCount = pendingMessages.Length;
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
                    if (_logger.IsEnabled( LogLevel.Debug )) {
                        string msgId = pending.MessageId.ToString( );
                        LogFoundEligibleMessage( _logger, msgId, stream );
                    }
                    return message;
                }

                blockedPendingCount++;
                // Message is blocked - leave it pending, don't acknowledge
                if (_logger.IsEnabled( LogLevel.Debug )) {
                    string msgIdString = pending.MessageId.ToString( );
                    LogSkippingBlockedMessage( _logger, msgIdString, stream );
                }
            }
        }

        // No eligible pending messages, read new messages using XREADGROUP
        // This properly claims messages from the stream (unlike XCLAIM which only works for pending messages)
        // Read a batch and check each for rate limiting
        const int MaxNewMessagesToRead = 50;
        int messagesChecked = 0;

        while (messagesChecked < MaxNewMessagesToRead) {
            // Read one message at a time so we can check rate limits and leave blocked ones pending
            StreamEntry[] newEntries = await db.StreamReadGroupAsync(
                stream,
                _consumerGroup,
                _consumerId,
                position: StreamPosition.NewMessages, // ">" - only new messages
                count: 1,
                noAck: false // Message becomes pending until acknowledged
            );

            if (newEntries.Length == 0) {
                // No more new messages in stream
                if (messagesChecked == 0 && _logger.IsEnabled( LogLevel.Debug )) {
                    LogNoNewMessagesInStream( _logger, stream );
                }
                break;
            }

            newMessagesRead++;
            messagesChecked++;

            StreamEntry entry = newEntries[0];
            QueuedMessage<T>? message = ParseStreamEntry( entry, stream );
            if (message is null) { continue; }

            // Check if this message's endpoint is blocked
            if (!IsMessageBlocked( message.Payload, blockedEndpoints )) {
                if (_logger.IsEnabled( LogLevel.Debug )) {
                    string msgId = entry.Id.ToString( );
                    LogFoundEligibleMessage( _logger, msgId, stream );
                }
                return message;
            }

            // Message is blocked by rate limiting - leave it pending for later retry
            blockedNewCount++;
            if (_logger.IsEnabled( LogLevel.Debug )) {
                string entryIdString = entry.Id.ToString( );
                LogSkippingRateLimitedMessage( _logger, entryIdString, stream );
            }
            // Continue to check next message
        }

        // Log summary of what we scanned
        if (_logger.IsEnabled( LogLevel.Debug ) && (pendingCount > 0 || newMessagesRead > 0)) {
            LogStreamScanSummary( _logger, stream, pendingCount, newMessagesRead, blockedPendingCount + blockedNewCount );
        }

        return null;
    }

    /// <summary>
    /// Determines if a message should be blocked based on its lookup type.
    /// </summary>
    private static bool IsMessageBlocked( T request, HashSet<string> blockedEndpoints ) {
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

        LogAcknowledged( _logger, id, stream );
    }

    /// <inheritdoc/>
    public async Task RequeueAsync( string messageId, TimeSpan? delay = null, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

        (string stream, string id) = ParseMessageId( messageId );
        IDatabase db = _redis.GetDatabase( );

        // Read the original message
        StreamEntry[] entries = await db.StreamRangeAsync( stream, id, id, count: 1 );

        if (entries.Length == 0) {
            LogMessageNotFoundForRequeue( _logger, id, stream );
            return;
        }

        StreamEntry original = entries[0];

        // Acknowledge and delete the original
        _ = await db.StreamAcknowledgeAsync( stream, _consumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        if (delay.HasValue && delay.Value > TimeSpan.Zero) {
            // For delayed requeue, we'd need a separate delay mechanism (e.g., sorted set with score = delivery time)
            // For now, add immediately with a note. Phase 4 workers can implement delay checking.
            LogDelayedRequeueNotImplemented( _logger );
        }

        // Re-add with updated enqueued time
        NameValueEntry[] fields = [
            new NameValueEntry( QueueStreamFieldNames.Payload, original[QueueStreamFieldNames.Payload] ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( stream, fields );

        // Record requeue metric
        QueueMetrics.RecordRequeue( _provider );

        LogRequeued( _logger, stream );
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
            new NameValueEntry( QueueStreamFieldNames.Payload, original[QueueStreamFieldNames.Payload] ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( targetStream, fields );

        // Delete from DLQ
        _ = await db.StreamDeleteAsync( _dlqStream, [id] );

        LogMovedFromDlq( _logger, id, targetStream, priority );
    }

    /// <inheritdoc/>
    public async Task MoveToDlqAsync( string messageId, string reason, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( messageId );

        (string stream, string id) = ParseMessageId( messageId );
        IDatabase db = _redis.GetDatabase( );

        // Read the original message
        StreamEntry[] entries = await db.StreamRangeAsync( stream, id, id, count: 1 );

        if (entries.Length == 0) {
            LogMessageNotFoundForDlqMove( _logger, id, stream );
            return;
        }

        StreamEntry original = entries[0];

        // Add to DLQ with metadata
        NameValueEntry[] dlqFields = [
            new NameValueEntry( QueueStreamFieldNames.Payload, original[QueueStreamFieldNames.Payload] ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, original[QueueStreamFieldNames.EnqueuedAt] ),
            new NameValueEntry( DlqOriginalStreamField, stream ),
            new NameValueEntry( DlqReasonField, reason ),
            new NameValueEntry( DlqMovedAtField, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];

        _ = await db.StreamAddAsync( _dlqStream, dlqFields );

        // Acknowledge and delete from original stream
        _ = await db.StreamAcknowledgeAsync( stream, _consumerGroup, id );
        _ = await db.StreamDeleteAsync( stream, [id] );

        LogMovedToDlq( _logger, id, stream, reason );
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
            LogDeletedFromDlq( _logger, id );
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

    /// <summary>
    /// Returns the ordered stream array for the current dequeue call, applying bulk gating and
    /// passing the current depth to the pure ordering seam.
    /// </summary>
    private async Task<string[]> GetWeightedStreamOrderWithBulkGatingAsync( int dequeueCounter ) {
        QueueDepth depth = await GetDepthAsync( );
        return GetStreamDequeueOrder( dequeueCounter, depth );
    }

    /// <summary>
    /// Pure method: returns the deterministic stream dequeue order given the current counter and queue depth.
    /// Interactive is served first on every non-aging call. On every <c>InteractiveAgingInterval</c>-th
    /// call a lower-priority tier leads (background and bulk alternate) to prevent starvation.
    /// Bulk is excluded when its depth is below the per-provider minimum threshold.
    /// </summary>
    /// <param name="dequeueCounter">The current dequeue ordering counter (incremented by caller before this call).</param>
    /// <param name="depth">Current queue depths used for bulk gating.</param>
    /// <returns>Ordered array of stream keys, highest effective priority first.</returns>
    internal string[] GetStreamDequeueOrder( int dequeueCounter, QueueDepth depth ) {
        // Bulk gating: exclude bulk stream if its depth is below the threshold.
        int minBulkThreshold = _settings.GetMinBulkThreshold( _provider );
        bool bulkEligible = minBulkThreshold <= 0 || depth.Bulk >= minBulkThreshold;

        if (!bulkEligible) {
            LogBulkGated( _logger, depth.Bulk, minBulkThreshold );
        }

        // Aging: every _agingInterval-th call, demote interactive and promote a lower tier.
        // Rotate between background (odd aging slots) and bulk (even aging slots) so neither starves.
        bool isAgingSlot = dequeueCounter % _agingInterval == 0;

        if (isAgingSlot) {
            // agingSlotIndex: 1 for first slot, 2 for second, …
            int agingSlotIndex = dequeueCounter / _agingInterval;
            bool backgroundLeads = (agingSlotIndex % 2) != 0;

            if (backgroundLeads) {
                // Background leads the aging slot.
                return bulkEligible
                    ? [_backgroundStream, _interactiveStream, _bulkStream]
                    : [_backgroundStream, _interactiveStream];
            } else {
                if (bulkEligible) {
                    // Bulk leads the aging slot.
                    return [_bulkStream, _interactiveStream, _backgroundStream];
                } else {
                    // Bulk not eligible — background leads anyway (no bulk to alternate with).
                    return [_backgroundStream, _interactiveStream];
                }
            }
        }

        // Normal case: interactive first.
        return bulkEligible
            ? [_interactiveStream, _backgroundStream, _bulkStream]
            : [_interactiveStream, _backgroundStream];
    }

    private QueuedMessage<T>? ParseStreamEntry( StreamEntry entry, string stream ) {
        string? payload = entry[QueueStreamFieldNames.Payload];
        string? enqueuedAtStr = entry[QueueStreamFieldNames.EnqueuedAt];

        if (string.IsNullOrEmpty( payload )) {
            LogMessageNoPayload( _logger, entry.Id.ToString( ), stream );
            return null;
        }

        try {
            T? request = JsonSerializer.Deserialize<T>( payload, _jsonOptions );
            if (request is null) {
                LogDeserializationFailed( _logger, entry.Id.ToString( ) );
                return null;
            }

            DateTimeOffset enqueuedAt = !string.IsNullOrEmpty( enqueuedAtStr )
                ? DateTimeOffset.Parse( enqueuedAtStr )
                : DateTimeOffset.UtcNow;

            // Composite message ID includes stream for ack/requeue
            string compositeId = $"{stream}:{entry.Id}";

            return new QueuedMessage<T>( compositeId, request, enqueuedAt );
        } catch (JsonException ex) {
            LogDeserializationError( _logger, ex, entry.Id.ToString( ), stream );
            return null;
        }
    }

    private static (string stream, string id) ParseMessageId( string compositeId ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( compositeId );

        // Redis stream IDs are in format "timestamp-sequence" (e.g., "1234567890123-0")
        // Our composite format is "stream:timestamp-sequence"
        // Use regex to parse: everything before the last colon is the stream name,
        // and the last part should match the Redis ID pattern (digits-digits)

        Match match = RedisQueuePatterns.RedisStreamIdPattern.Match( compositeId );
        if (!match.Success) {
            throw new ArgumentException(
                $"Invalid composite message ID format. Expected 'stream:timestamp-sequence', got: {compositeId}",
                nameof( compositeId ) );
        }

        string stream = match.Groups[1].Value;
        string id = match.Groups[2].Value;

        return (stream, id);
    }

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueConsumerGroupCreated,
        Level = LogLevel.Information,
        Message = "Created consumer group {Group} for stream {Stream}" )]
    internal static partial void LogConsumerGroupCreated( ILogger logger, string group, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueConsumerGroupExists,
        Level = LogLevel.Debug,
        Message = "Consumer group {Group} already exists for stream {Stream}" )]
    internal static partial void LogConsumerGroupExists( ILogger logger, string group, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueEnqueued,
        Level = LogLevel.Debug,
        Message = "Enqueued message {MessageId} to {Stream} with priority {Priority}" )]
    internal static partial void LogEnqueued( ILogger logger, string messageId, string stream, QueuePriority priority );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueSkippingBlockedMessage,
        Level = LogLevel.Debug,
        Message = "Skipping rate-limited message {MessageId} in {Stream} - endpoint is blocked" )]
    internal static partial void LogSkippingBlockedMessage( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueSkippingRateLimitedMessage,
        Level = LogLevel.Debug,
        Message = "Skipping rate-limited message {MessageId} in {Stream}" )]
    internal static partial void LogSkippingRateLimitedMessage( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueAcknowledged,
        Level = LogLevel.Debug,
        Message = "Acknowledged and deleted message {MessageId} from {Stream}" )]
    internal static partial void LogAcknowledged( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMessageNotFoundForRequeue,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} not found in {Stream} for requeue" )]
    internal static partial void LogMessageNotFoundForRequeue( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDelayedRequeueNotImplemented,
        Level = LogLevel.Debug,
        Message = "Delayed requeue requested but not yet implemented. Adding immediately." )]
    internal static partial void LogDelayedRequeueNotImplemented( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueRequeued,
        Level = LogLevel.Debug,
        Message = "Requeued message from {Stream}" )]
    internal static partial void LogRequeued( ILogger logger, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMovedFromDlq,
        Level = LogLevel.Information,
        Message = "Moved message {MessageId} from DLQ to {Stream} with priority {Priority}" )]
    internal static partial void LogMovedFromDlq( ILogger logger, string messageId, string stream, QueuePriority priority );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMessageNotFoundForDlqMove,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} not found in {Stream} for DLQ move" )]
    internal static partial void LogMessageNotFoundForDlqMove( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMovedToDlq,
        Level = LogLevel.Warning,
        Message = "Moved message {MessageId} from {Stream} to DLQ. Reason: {Reason}" )]
    internal static partial void LogMovedToDlq( ILogger logger, string messageId, string stream, string reason );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDeletedFromDlq,
        Level = LogLevel.Information,
        Message = "Deleted message {MessageId} from DLQ" )]
    internal static partial void LogDeletedFromDlq( ILogger logger, string messageId );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueBulkGated,
        Level = LogLevel.Debug,
        Message = "Gating bulk operations - queue depth ({Depth}) below threshold ({Threshold})" )]
    internal static partial void LogBulkGated( ILogger logger, int depth, int threshold );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMessageNoPayload,
        Level = LogLevel.Warning,
        Message = "Message {Id} in {Stream} has no payload" )]
    internal static partial void LogMessageNoPayload( ILogger logger, string id, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDeserializationFailed,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize message {Id} payload" )]
    internal static partial void LogDeserializationFailed( ILogger logger, string id );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDeserializationError,
        Level = LogLevel.Error,
        Message = "Failed to deserialize message {Id} from {Stream}" )]
    internal static partial void LogDeserializationError( ILogger logger, Exception ex, string id, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDequeueStarting,
        Level = LogLevel.Debug,
        Message = "Rate-limit-aware dequeue starting with {BlockedCount} blocked endpoints, searching {StreamCount} streams" )]
    internal static partial void LogDequeueStarting( ILogger logger, int blockedCount, int streamCount );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDequeueNoEligibleMessages,
        Level = LogLevel.Debug,
        Message = "Dequeue completed with no eligible messages after searching {StreamCount} streams" )]
    internal static partial void LogDequeueNoEligibleMessages( ILogger logger, int streamCount );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueStreamScanSummary,
        Level = LogLevel.Debug,
        Message = "Stream {Stream} scan: {PendingCount} pending, {NewCount} new entries, {BlockedCount} blocked" )]
    internal static partial void LogStreamScanSummary( ILogger logger, string stream, int pendingCount, int newCount, int blockedCount );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueClaimFailed,
        Level = LogLevel.Warning,
        Message = "XCLAIM failed for message {MessageId} in stream {Stream} - message may not be pending (new messages cannot be claimed with XCLAIM)" )]
    internal static partial void LogClaimFailed( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueFoundEligibleMessage,
        Level = LogLevel.Debug,
        Message = "Found eligible message {MessageId} in stream {Stream}" )]
    internal static partial void LogFoundEligibleMessage( ILogger logger, string messageId, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueNoNewMessagesInStream,
        Level = LogLevel.Debug,
        Message = "No messages found in stream {Stream}" )]
    internal static partial void LogNoNewMessagesInStream( ILogger logger, string stream );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueAgingIntervalMisconfigured,
        Level = LogLevel.Warning,
        Message = "InteractiveAgingInterval {ConfiguredValue} is ≤ 1 (misconfiguration); falling back to default {DefaultValue}" )]
    internal static partial void LogAgingIntervalMisconfigured( ILogger logger, int configuredValue, int defaultValue );

    #endregion
}
