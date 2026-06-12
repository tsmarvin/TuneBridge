using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using QueueFieldNames = BridgeBeats.Core.Infrastructure.Queue.QueueStreamFieldNames;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Decorates the Spotify <see cref="IRequestQueue{T}"/> to route typed ID lookups
/// directly into the Spotify type-specific bulk streams, bypassing the generic
/// priority queue.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="EnqueueAsync"/> is called with a <see cref="QueuedLookupRequest"/>
/// whose <see cref="QueuedLookupRequest.LookupType"/> is
/// <see cref="LookupRequestType.SongIdLookup"/> or
/// <see cref="LookupRequestType.AlbumIdLookup"/>, this decorator writes directly to
/// <c>queue:spotify:bulk:track-id</c> (<see cref="SpotifyConstants.BulkTrackIdStream"/>)
/// or <c>queue:spotify:bulk:album-id</c> (<see cref="SpotifyConstants.BulkAlbumIdStream"/>)
/// respectively, using the same field shape (<c>payload</c> + <c>enqueuedAt</c>)
/// that <c>RedisRequestQueue</c> uses. All other call sites — dequeue, acknowledge,
/// requeue, depth, DLQ — are delegated unchanged to the inner queue.
/// </para>
/// <para>
/// Race safety: both <c>SpotifyBulkProcessorService</c> and the generic
/// <c>QueueProcessorBackgroundService</c> share the consumer group
/// <c>spotify-workers</c>, but they read from <em>disjoint</em> streams:
/// the type-specific bulk streams (<c>queue:spotify:bulk:track-id</c> /
/// <c>queue:spotify:bulk:album-id</c>) are only consumed by
/// <c>SpotifyBulkProcessorService</c>, while the generic priority streams
/// (<c>queue:spotify:interactive</c> / <c>queue:spotify:background</c> /
/// <c>queue:spotify:bulk</c>) are only consumed by the generic worker.
/// Disjoint streams with a single consumer service per stream is the correct
/// safety invariant — consumer group membership alone is not sufficient.
/// </para>
/// <para>
/// Registration: this decorator is applied automatically by
/// <c>QueueServiceExtensions.ApplySpotifyBulkDecorator</c>, which is called from both
/// <c>CreateProviderQueue</c> and the <c>AddAllProviderQueues</c> resolver factory.
/// Producers do not need any extra registration step.
/// </para>
/// </remarks>
public sealed partial class SpotifyBulkQueueDecorator : IRequestQueue<QueuedLookupRequest> {

    private readonly IRequestQueue<QueuedLookupRequest> _inner;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SpotifyBulkQueueDecorator> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyBulkQueueDecorator"/> class.
    /// </summary>
    /// <param name="inner">The underlying Spotify request queue.</param>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public SpotifyBulkQueueDecorator(
        IRequestQueue<QueuedLookupRequest> inner,
        IConnectionMultiplexer redis,
        ILogger<SpotifyBulkQueueDecorator> logger
    ) {
        _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Intercepts <see cref="LookupRequestType.SongIdLookup"/> and
    /// <see cref="LookupRequestType.AlbumIdLookup"/> requests and writes them
    /// to the type-specific bulk streams. All other lookup types are forwarded
    /// to the underlying queue unchanged.
    /// </remarks>
    public async Task EnqueueAsync(
        QueuedLookupRequest request,
        QueuePriority priority,
        CancellationToken cancellationToken = default
    ) {
        if (request.LookupType is LookupRequestType.SongIdLookup or LookupRequestType.AlbumIdLookup) {
            // Invariant: interactive lookups never wait. SongIdLookup/AlbumIdLookup are always
            // routed to the 24 h-linger bulk stream regardless of the priority argument, so a
            // caller that passes QueuePriority.Interactive will wait up to 24 h — not the
            // interactive wait budget. Callers must never pass SupportedProviders.Spotify to
            // the interactive provider-ID entry point (ICachingMediaLinkService.GetInfoByProviderIdAsync).
            if (priority == QueuePriority.Interactive) {
                LogInteractivePriorityOnBulkStream( _logger, request.LookupType, request.SagaId );
            }

            string stream = request.LookupType == LookupRequestType.SongIdLookup
                ? SpotifyConstants.BulkTrackIdStream
                : SpotifyConstants.BulkAlbumIdStream;

            string payload = JsonSerializer.Serialize( request, _jsonOptions );

            IDatabase db = _redis.GetDatabase( );
            NameValueEntry[] fields = [
                new NameValueEntry( QueueFieldNames.Payload, payload ),
                new NameValueEntry( QueueFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
            ];

            _ = await db.StreamAddAsync( stream, fields );

            // Record enqueue metric with QueuePriority.Bulk so dashboards can distinguish
            // bulk-stream enqueues from generic interactive/background ones.
            QueueMetrics.RecordEnqueue( SupportedProviders.Spotify, QueuePriority.Bulk );

            LogRoutedToBulkStream( _logger, request.LookupType, request.SagaId, stream );

            return;
        }

        // All other lookup types (UriLookup, IsrcLookup, UpcLookup, etc.) pass through
        await _inner.EnqueueAsync( request, priority, cancellationToken );
    }

    /// <inheritdoc/>
    public Task<QueuedMessage<QueuedLookupRequest>?> DequeueAsync( CancellationToken cancellationToken = default )
        => _inner.DequeueAsync( cancellationToken );

    /// <inheritdoc/>
    public Task<QueuedMessage<QueuedLookupRequest>?> DequeueAsync( IRateLimitTracker rateLimitTracker, CancellationToken cancellationToken = default )
        => _inner.DequeueAsync( rateLimitTracker, cancellationToken );

    /// <inheritdoc/>
    public Task AcknowledgeAsync( string messageId, CancellationToken cancellationToken = default )
        => _inner.AcknowledgeAsync( messageId, cancellationToken );

    /// <inheritdoc/>
    public Task RequeueAsync( string messageId, TimeSpan? delay = null, CancellationToken cancellationToken = default )
        => _inner.RequeueAsync( messageId, delay, cancellationToken );

    /// <inheritdoc/>
    public Task<QueueDepth> GetDepthAsync( CancellationToken cancellationToken = default )
        => _inner.GetDepthAsync( cancellationToken );

    /// <inheritdoc/>
    public Task<IReadOnlyList<QueuedMessage<QueuedLookupRequest>>> GetDlqMessagesAsync( int limit, CancellationToken cancellationToken = default )
        => _inner.GetDlqMessagesAsync( limit, cancellationToken );

    /// <inheritdoc/>
    public Task RequeueFromDlqAsync( string messageId, QueuePriority priority, CancellationToken cancellationToken = default )
        => _inner.RequeueFromDlqAsync( messageId, priority, cancellationToken );

    /// <inheritdoc/>
    public Task MoveToDlqAsync( string messageId, string reason, CancellationToken cancellationToken = default )
        => _inner.MoveToDlqAsync( messageId, reason, cancellationToken );

    /// <inheritdoc/>
    public Task<bool> DeleteFromDlqAsync( string messageId, CancellationToken cancellationToken = default )
        => _inner.DeleteFromDlqAsync( messageId, cancellationToken );

    #region LoggerMessage Methods

    /// <summary>
    /// Logs that a typed ID lookup was routed to a type-specific bulk stream.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "SpotifyBulkQueueDecorator routed {LookupType} (SagaId={SagaId}) to {Stream}" )]
    private static partial void LogRoutedToBulkStream(
        ILogger logger,
        LookupRequestType lookupType,
        string sagaId,
        string stream );

    /// <summary>
    /// Logs a warning when a SongIdLookup or AlbumIdLookup arrives with Interactive priority.
    /// The item still routes to the 24 h-linger bulk stream — callers must never pass
    /// SupportedProviders.Spotify to the interactive provider-ID entry point.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.SpotifyBulkDecoratorInteractivePriority,
        Level = LogLevel.Warning,
        Message = "SpotifyBulkQueueDecorator received {LookupType} (SagaId={SagaId}) with Interactive priority; item routes to the 24 h-linger bulk stream — interactive caller will not receive a timely result" )]
    private static partial void LogInteractivePriorityOnBulkStream(
        ILogger logger,
        LookupRequestType lookupType,
        string sagaId );

    #endregion
}
