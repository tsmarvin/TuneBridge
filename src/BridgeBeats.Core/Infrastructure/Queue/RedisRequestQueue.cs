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
/// Helper class containing the compiled regular expressions used by Redis queue operations.
/// Separate from the generic <see cref="RedisRequestQueue{T}"/> to avoid a static field in a
/// generic type.
/// </summary>
internal static partial class RedisQueuePatterns {
    /// <summary>
    /// Matches a composite message id of the form <c>{streamName}:{redisId}</c>, where the Redis
    /// stream id is a <c>timestamp-sequence</c> pair. Group 1 is the stream name, group 2 is the
    /// Redis stream id. The stream name must contain at least one character.
    /// </summary>
    internal static readonly Regex RedisStreamIdPattern = GenerateRedisStreamIdPattern( );

    /// <summary>Source generator for <see cref="RedisStreamIdPattern"/>.</summary>
    /// <returns>The compiled composite-id pattern.</returns>
    [GeneratedRegex( @"^(.+):(\d+-\d+)$" )]
    private static partial Regex GenerateRedisStreamIdPattern( );
}

/// <summary>
/// Redis Streams-backed implementation of <see cref="IRequestQueue{T}"/> for a single music
/// provider, with priority lanes and a dead-letter queue.
/// </summary>
/// <typeparam name="T">The queued request type; must be a reference type implementing the queueable contract.</typeparam>
/// <remarks>
/// Each provider gets four streams: <c>queue:{provider}:interactive</c>,
/// <c>:background</c>, <c>:bulk</c>, and <c>:dlq</c> (provider name lower-cased). Messages are read
/// through a per-provider consumer group (<c>{provider}-workers</c>) by a per-process consumer
/// (<c>{provider}-worker-{guid}</c>). Two behaviors are easy to misread and are called out here:
/// acknowledgement does XACK followed by XDEL, so a processed message is deleted and cannot be
/// replayed from the stream; and the delayed form of requeue is a logged stub that requeues
/// immediately, so the requested delay is not honored. The unit other components pass around is
/// the composite message id <c>{streamName}:{redisId}</c>.
/// </remarks>
public sealed partial class RedisRequestQueue<T> : IRequestQueue<T> where T : class, IQueueableRequest {

    /// <summary>Redis connection used for all stream operations.</summary>
    private readonly IConnectionMultiplexer _redis;

    /// <summary>Logger for queue diagnostics.</summary>
    private readonly ILogger<RedisRequestQueue<T>> _logger;

    /// <summary>Queue configuration (aging interval, bulk thresholds, job expiry, and so on).</summary>
    private readonly QueueSettings _settings;

    /// <summary>The provider this queue instance serves.</summary>
    private readonly SupportedProviders _provider;

    /// <summary>The Redis consumer group name (<c>{provider}-workers</c>).</summary>
    private readonly string _consumerGroup;

    /// <summary>This process's unique consumer id (<c>{provider}-worker-{guid}</c>).</summary>
    private readonly string _consumerId;

    /// <summary>Camel-case, non-indented options used to serialize and deserialize payloads.</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    // Stream keys

    /// <summary>Stream key for the interactive (highest) priority lane.</summary>
    private readonly string _interactiveStream;

    /// <summary>Stream key for the background priority lane.</summary>
    private readonly string _backgroundStream;

    /// <summary>Stream key for the bulk (lowest) priority lane.</summary>
    private readonly string _bulkStream;

    /// <summary>Stream key for the dead-letter queue.</summary>
    private readonly string _dlqStream;

    /// <summary>
    /// Effective aging interval (validated in constructor; N ≤ 1 falls back to default). Every
    /// aging slot promotes background/bulk ahead of interactive (minimum 2).
    /// </summary>
    private readonly int _agingInterval;

    /// <summary>
    /// Per-instance dequeue ordering counter that drives aging-slot selection. There is a single
    /// consumer per queue instance, so no volatile is required; the unchecked increment wraps at
    /// int.MaxValue harmlessly because the modulo arithmetic continues to work.
    /// </summary>
    private int _dequeueCounter;

    // Track which stream a message came from for ack/requeue

    /// <summary>Dead-letter field holding the reason a message was dead-lettered. Literal: <c>"dlqReason"</c>.</summary>
    private const string DlqReasonField = "dlqReason";

    /// <summary>Dead-letter field holding when the message was dead-lettered. Literal: <c>"dlqMovedAt"</c>.</summary>
    private const string DlqMovedAtField = "dlqMovedAt";

    /// <summary>Dead-letter field holding the stream the message came from. Literal: <c>"originalStream"</c>.</summary>
    private const string DlqOriginalStreamField = "originalStream";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisRequestQueue{T}"/> class for one provider,
    /// derives its stream and consumer names, and validates the aging interval.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="settings">Queue configuration settings.</param>
    /// <param name="provider">The provider this queue serves.</param>
    /// <remarks>
    /// A configured aging interval of 1 or less is treated as a misconfiguration and replaced with
    /// a default of 8 (logged), keeping the starvation-prevention logic well-defined.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redis"/>, <paramref name="logger"/>, or <paramref name="settings"/> is null.</exception>
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
    /// Ensures consumer groups exist for all priority streams. Call this during worker startup.
    /// </summary>
    /// <param name="cancellationToken">Token checked between streams to stop early.</param>
    /// <returns>A task that completes once each stream has the consumer group.</returns>
    /// <remarks>
    /// Creates the stream if absent. A <c>BUSYGROUP</c> error (group already exists) is caught and
    /// treated as success. The dead-letter stream does not get a consumer group; it is read by range.
    /// </remarks>
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

    /// <summary>
    /// Serializes a request and appends it to the stream for the given priority.
    /// </summary>
    /// <param name="request">The request to enqueue.</param>
    /// <param name="priority">The priority lane to enqueue onto.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the entry is added.</returns>
    /// <remarks>
    /// The entry carries <c>payload</c> (JSON) and <c>enqueuedAt</c> (ISO-8601 UTC). An enqueue
    /// metric is recorded for the provider and priority.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
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

    /// <summary>
    /// Dequeues the next message in priority order (ignoring rate-limit state).
    /// </summary>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>The next message, or null when all eligible streams are empty.</returns>
    /// <remarks>
    /// Streams are visited in the order produced by the weighted, bulk-gated scheduler, so this
    /// reflects the same aging and bulk-threshold behavior as the rate-limit-aware overload. A
    /// dequeue metric is recorded when a message is returned.
    /// </remarks>
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

    /// <summary>
    /// Dequeues the next message whose endpoint is not currently rate-limited.
    /// </summary>
    /// <param name="rateLimitTracker">Tracker queried for the provider's currently blocked endpoints.</param>
    /// <param name="cancellationToken">Token forwarded to the rate-limit lookup.</param>
    /// <returns>The next eligible message, or null when no unblocked message is available.</returns>
    /// <remarks>
    /// Reads the set of blocked endpoints once, then visits streams in priority order, skipping
    /// messages whose lookup type maps to a blocked endpoint. A dequeue metric is recorded when an
    /// eligible message is returned.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rateLimitTracker"/> is null.</exception>
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
    /// Finds the first eligible message in the stream that is not rate-limited, checking the
    /// consumer's pending list before reading new messages via XREADGROUP.
    /// </summary>
    /// <param name="db">The Redis database to read from.</param>
    /// <param name="stream">The stream to scan.</param>
    /// <param name="blockedEndpoints">Endpoints currently rate-limited and therefore skipped.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>The first eligible message, or null if none found within the scan caps.</returns>
    /// <remarks>
    /// Pending messages already claimed by this consumer are checked first (up to 50, recovery
    /// scenario), then up to 50 new messages are read. Blocked messages are left in place to be
    /// retried once their endpoint clears. The scan caps bound the per-stream work each dequeue does.
    /// </remarks>
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
    /// Determines whether a request targets a currently rate-limited (blocked) endpoint, based on
    /// its lookup type.
    /// </summary>
    /// <param name="request">The request to test.</param>
    /// <param name="blockedEndpoints">The set of blocked endpoint keys.</param>
    /// <returns>
    /// True if the request is a lookup request whose lookup type matches a blocked endpoint;
    /// otherwise false. Non-lookup request types are never treated as blocked.
    /// </returns>
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

    /// <summary>
    /// Acknowledges a processed message: XACK to the consumer group, then XDEL to remove it.
    /// </summary>
    /// <param name="messageId">The composite message id to acknowledge.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the message is acknowledged and deleted.</returns>
    /// <remarks>
    /// Because acknowledgement deletes the message (XACK + XDEL), the stream does not retain
    /// processed entries and a message cannot be replayed from the stream itself. Any retry must
    /// re-enqueue a fresh entry. An acknowledge metric is recorded.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null or whitespace, or is not a valid composite id.</exception>
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

    /// <summary>
    /// Requeues a message back onto its original stream with a fresh enqueue timestamp.
    /// </summary>
    /// <param name="messageId">The composite message id to requeue.</param>
    /// <param name="delay">
    /// Requested delay before the message becomes available again. <b>Not honored:</b> a non-zero
    /// delay is logged and then ignored, and the message is requeued immediately.
    /// </param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the message is requeued, or immediately if the original entry no longer exists.</returns>
    /// <remarks>
    /// Reads the original entry, acknowledges and deletes it, then re-adds the same payload with a
    /// new <c>enqueuedAt</c>. The delayed-requeue path is a known stub: it only logs that delay is
    /// not implemented and proceeds with an immediate requeue. A requeue metric is recorded.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null or whitespace, or is not a valid composite id.</exception>
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
            // For now, add immediately with a note. Workers can implement delay checking later.
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

    /// <summary>
    /// Returns the current depth of each priority lane and their total.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A <see cref="QueueDepth"/> with the interactive, background, bulk, and total lengths.</returns>
    /// <remarks>The dead-letter stream is not included in the total.</remarks>
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

    /// <summary>
    /// Reads messages from the dead-letter queue by range.
    /// </summary>
    /// <param name="limit">Maximum number to return; a non-positive value defaults to 100.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>The parsed dead-letter messages, skipping any entry that fails to parse.</returns>
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

    /// <summary>
    /// Moves a message out of the dead-letter queue and back onto a live priority lane.
    /// </summary>
    /// <param name="messageId">The composite dead-letter message id.</param>
    /// <param name="priority">The priority lane to requeue the message onto.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the message is requeued and removed from the dead-letter queue.</returns>
    /// <remarks>The original payload is re-added with a fresh <c>enqueuedAt</c>; the dead-letter metadata fields are dropped.</remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null/whitespace or does not belong to the dead-letter queue.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the message is not found in the dead-letter queue.</exception>
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

    /// <summary>
    /// Moves a message from a live stream into the dead-letter queue, recording why.
    /// </summary>
    /// <param name="messageId">The composite message id to dead-letter.</param>
    /// <param name="reason">The reason recorded on the dead-letter entry.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>A task that completes once the message is dead-lettered, or immediately if the original entry no longer exists.</returns>
    /// <remarks>
    /// The dead-letter entry preserves the original <c>payload</c> and <c>enqueuedAt</c> and adds
    /// <c>originalStream</c>, <c>dlqReason</c>, and <c>dlqMovedAt</c>. The source message is then
    /// acknowledged and deleted from its stream.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null or whitespace, or is not a valid composite id.</exception>
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

    /// <summary>
    /// Permanently deletes a message from the dead-letter queue.
    /// </summary>
    /// <param name="messageId">The composite dead-letter message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token (not currently observed).</param>
    /// <returns>True if a message was deleted; false if the id is not a dead-letter id or nothing was deleted.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="messageId"/> is null or whitespace, or is not a valid composite id.</exception>
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

    /// <summary>Maps a priority to its stream key.</summary>
    /// <param name="priority">The priority to map.</param>
    /// <returns>The stream key for that priority.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognized priority.</exception>
    private string GetStreamForPriority( QueuePriority priority ) => priority switch {
        QueuePriority.Interactive => _interactiveStream,
        QueuePriority.Background => _backgroundStream,
        QueuePriority.Bulk => _bulkStream,
        _ => throw new ArgumentOutOfRangeException( nameof( priority ) )
    };

    /// <summary>Maps a stream key back to its priority, defaulting to background for unknown streams.</summary>
    /// <param name="stream">The stream key to map.</param>
    /// <returns>The priority the stream represents.</returns>
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
    /// <param name="dequeueCounter">The current dequeue counter that drives aging-slot selection.</param>
    /// <returns>The ordered stream keys to attempt for this dequeue.</returns>
    private async Task<string[]> GetWeightedStreamOrderWithBulkGatingAsync( int dequeueCounter ) {
        QueueDepth depth = await GetDepthAsync( );
        return GetStreamDequeueOrder( dequeueCounter, depth );
    }

    /// <summary>
    /// Pure method: returns the deterministic stream dequeue order given the current counter and
    /// queue depth, balancing priority against starvation prevention and bulk batching.
    /// </summary>
    /// <param name="dequeueCounter">The current dequeue ordering counter (incremented by the caller before this call); every <c>InteractiveAgingInterval</c>-th value is an aging slot.</param>
    /// <param name="depth">Current queue depths used for bulk gating.</param>
    /// <returns>Ordered array of stream keys, highest effective priority first.</returns>
    /// <remarks>
    /// Interactive is served first on every non-aging call. On every aging slot a lower-priority
    /// tier leads (background and bulk alternate across successive aging slots) to prevent
    /// starvation. Bulk is only included once its depth reaches the per-provider minimum threshold
    /// (gating lets bulk accumulate for batch efficiency); below the threshold, bulk is omitted from
    /// the order.
    /// </remarks>
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

    /// <summary>
    /// Parses a raw stream entry into a typed message, building the composite id and recovering the
    /// enqueue time.
    /// </summary>
    /// <param name="entry">The raw stream entry.</param>
    /// <param name="stream">The stream the entry came from (used to build the composite id).</param>
    /// <returns>The parsed message, or null if the entry has no payload or fails to deserialize.</returns>
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

    /// <summary>
    /// Splits a composite message id (<c>{stream}:{timestamp-sequence}</c>) into its stream name and
    /// Redis stream id.
    /// </summary>
    /// <param name="compositeId">The composite id to parse.</param>
    /// <returns>A tuple of the stream name and the Redis stream id.</returns>
    /// <exception cref="ArgumentException">Thrown when the id is null/whitespace or does not match the expected format.</exception>
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

    /// <summary>Logs that a consumer group was created for a stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="group">The consumer group name.</param>
    /// <param name="stream">The stream the group was created on.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueConsumerGroupCreated,
        Level = LogLevel.Information,
        Message = "Created consumer group {Group} for stream {Stream}" )]
    internal static partial void LogConsumerGroupCreated( ILogger logger, string group, string stream );

    /// <summary>Logs that a consumer group already existed for a stream (BUSYGROUP).</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="group">The consumer group name.</param>
    /// <param name="stream">The stream that already had the group.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueConsumerGroupExists,
        Level = LogLevel.Debug,
        Message = "Consumer group {Group} already exists for stream {Stream}" )]
    internal static partial void LogConsumerGroupExists( ILogger logger, string group, string stream );

    /// <summary>Logs that a message was enqueued.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The Redis stream id of the new entry.</param>
    /// <param name="stream">The stream the message was added to.</param>
    /// <param name="priority">The priority lane.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueEnqueued,
        Level = LogLevel.Debug,
        Message = "Enqueued message {MessageId} to {Stream} with priority {Priority}" )]
    internal static partial void LogEnqueued( ILogger logger, string messageId, string stream, QueuePriority priority );

    /// <summary>Logs that a pending message was skipped because its endpoint is blocked.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The skipped message id.</param>
    /// <param name="stream">The stream being scanned.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueSkippingBlockedMessage,
        Level = LogLevel.Debug,
        Message = "Skipping rate-limited message {MessageId} in {Stream} - endpoint is blocked" )]
    internal static partial void LogSkippingBlockedMessage( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a newly read message was skipped because its endpoint is rate-limited.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The skipped message id.</param>
    /// <param name="stream">The stream being scanned.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueSkippingRateLimitedMessage,
        Level = LogLevel.Debug,
        Message = "Skipping rate-limited message {MessageId} in {Stream}" )]
    internal static partial void LogSkippingRateLimitedMessage( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message was acknowledged and deleted (XACK + XDEL).</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The acknowledged message id.</param>
    /// <param name="stream">The stream the message was in.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueAcknowledged,
        Level = LogLevel.Debug,
        Message = "Acknowledged and deleted message {MessageId} from {Stream}" )]
    internal static partial void LogAcknowledged( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message to requeue was not found in its stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The missing message id.</param>
    /// <param name="stream">The stream searched.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMessageNotFoundForRequeue,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} not found in {Stream} for requeue" )]
    internal static partial void LogMessageNotFoundForRequeue( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a delayed requeue was requested but the delay is not implemented (the message is requeued immediately).</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDelayedRequeueNotImplemented,
        Level = LogLevel.Debug,
        Message = "Delayed requeue requested but not yet implemented. Adding immediately." )]
    internal static partial void LogDelayedRequeueNotImplemented( ILogger logger );

    /// <summary>Logs that a message was requeued.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="stream">The stream the message was requeued onto.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueRequeued,
        Level = LogLevel.Debug,
        Message = "Requeued message from {Stream}" )]
    internal static partial void LogRequeued( ILogger logger, string stream );

    /// <summary>Logs that a message was moved out of the dead-letter queue onto a live lane.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The dead-letter message id.</param>
    /// <param name="stream">The target stream.</param>
    /// <param name="priority">The target priority.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMovedFromDlq,
        Level = LogLevel.Information,
        Message = "Moved message {MessageId} from DLQ to {Stream} with priority {Priority}" )]
    internal static partial void LogMovedFromDlq( ILogger logger, string messageId, string stream, QueuePriority priority );

    /// <summary>Logs that a message to dead-letter was not found in its stream.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The missing message id.</param>
    /// <param name="stream">The stream searched.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMessageNotFoundForDlqMove,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} not found in {Stream} for DLQ move" )]
    internal static partial void LogMessageNotFoundForDlqMove( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a message was moved into the dead-letter queue.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The dead-lettered message id.</param>
    /// <param name="stream">The originating stream.</param>
    /// <param name="reason">The dead-letter reason.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMovedToDlq,
        Level = LogLevel.Warning,
        Message = "Moved message {MessageId} from {Stream} to DLQ. Reason: {Reason}" )]
    internal static partial void LogMovedToDlq( ILogger logger, string messageId, string stream, string reason );

    /// <summary>Logs that a message was deleted from the dead-letter queue.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The deleted message id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDeletedFromDlq,
        Level = LogLevel.Information,
        Message = "Deleted message {MessageId} from DLQ" )]
    internal static partial void LogDeletedFromDlq( ILogger logger, string messageId );

    /// <summary>Logs that the bulk lane was gated because its depth is below the threshold.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="depth">The current bulk depth.</param>
    /// <param name="threshold">The minimum bulk threshold.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueBulkGated,
        Level = LogLevel.Debug,
        Message = "Gating bulk operations - queue depth ({Depth}) below threshold ({Threshold})" )]
    internal static partial void LogBulkGated( ILogger logger, int depth, int threshold );

    /// <summary>Logs that a stream entry had no payload field and was ignored.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="id">The entry id.</param>
    /// <param name="stream">The stream the entry was in.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueMessageNoPayload,
        Level = LogLevel.Warning,
        Message = "Message {Id} in {Stream} has no payload" )]
    internal static partial void LogMessageNoPayload( ILogger logger, string id, string stream );

    /// <summary>Logs that a payload deserialized to null.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="id">The entry id.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDeserializationFailed,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize message {Id} payload" )]
    internal static partial void LogDeserializationFailed( ILogger logger, string id );

    /// <summary>Logs that a payload failed to deserialize with an exception.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The deserialization exception.</param>
    /// <param name="id">The entry id.</param>
    /// <param name="stream">The stream the entry was in.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDeserializationError,
        Level = LogLevel.Error,
        Message = "Failed to deserialize message {Id} from {Stream}" )]
    internal static partial void LogDeserializationError( ILogger logger, Exception ex, string id, string stream );

    /// <summary>Logs the start of a rate-limit-aware dequeue.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="blockedCount">Number of blocked endpoints.</param>
    /// <param name="streamCount">Number of streams to search.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDequeueStarting,
        Level = LogLevel.Debug,
        Message = "Rate-limit-aware dequeue starting with {BlockedCount} blocked endpoints, searching {StreamCount} streams" )]
    internal static partial void LogDequeueStarting( ILogger logger, int blockedCount, int streamCount );

    /// <summary>Logs that a dequeue found no eligible message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="streamCount">Number of streams that were searched.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueDequeueNoEligibleMessages,
        Level = LogLevel.Debug,
        Message = "Dequeue completed with no eligible messages after searching {StreamCount} streams" )]
    internal static partial void LogDequeueNoEligibleMessages( ILogger logger, int streamCount );

    /// <summary>Logs a per-stream scan summary.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="stream">The stream scanned.</param>
    /// <param name="pendingCount">Pending entries examined.</param>
    /// <param name="newCount">New entries read.</param>
    /// <param name="blockedCount">Entries skipped because their endpoint was blocked.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueStreamScanSummary,
        Level = LogLevel.Debug,
        Message = "Stream {Stream} scan: {PendingCount} pending, {NewCount} new entries, {BlockedCount} blocked" )]
    internal static partial void LogStreamScanSummary( ILogger logger, string stream, int pendingCount, int newCount, int blockedCount );

    /// <summary>Logs that an XCLAIM attempt failed (for example because the message is not pending).</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The message id that could not be claimed.</param>
    /// <param name="stream">The stream involved.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueClaimFailed,
        Level = LogLevel.Warning,
        Message = "XCLAIM failed for message {MessageId} in stream {Stream} - message may not be pending (new messages cannot be claimed with XCLAIM)" )]
    internal static partial void LogClaimFailed( ILogger logger, string messageId, string stream );

    /// <summary>Logs that an eligible (unblocked) message was found.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageId">The eligible message id.</param>
    /// <param name="stream">The stream it was found in.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueFoundEligibleMessage,
        Level = LogLevel.Debug,
        Message = "Found eligible message {MessageId} in stream {Stream}" )]
    internal static partial void LogFoundEligibleMessage( ILogger logger, string messageId, string stream );

    /// <summary>Logs that a stream had no new messages.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="stream">The empty stream.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueNoNewMessagesInStream,
        Level = LogLevel.Debug,
        Message = "No messages found in stream {Stream}" )]
    internal static partial void LogNoNewMessagesInStream( ILogger logger, string stream );

    /// <summary>Logs that a misconfigured aging interval (1 or less) was replaced with the default.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="configuredValue">The configured (invalid) value.</param>
    /// <param name="defaultValue">The default value applied instead.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestQueueAgingIntervalMisconfigured,
        Level = LogLevel.Warning,
        Message = "InteractiveAgingInterval {ConfiguredValue} is ≤ 1 (misconfiguration); falling back to default {DefaultValue}" )]
    internal static partial void LogAgingIntervalMisconfigured( ILogger logger, int configuredValue, int defaultValue );

    #endregion
}
