using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Utilities;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Redis-based implementation of <see cref="IRequestDeduplicator"/> using SETNX for locking
/// and pub/sub for completion notification.
/// </summary>
/// <remarks>
/// <para>
/// Key patterns:
/// <list type="bullet">
///   <item><c>inflight:{requestKey}</c> - SETNX lock with processing instance ID</item>
///   <item><c>complete:{requestKey}</c> - Pub/sub channel for completion notification</item>
/// </list>
/// </para>
/// <para>
/// When a request is in-flight, other instances can subscribe to the completion channel
/// to be notified when the result is available, avoiding redundant API calls.
/// </para>
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="RedisRequestDeduplicator"/> class.
/// </remarks>
/// <param name="redis">The Redis connection multiplexer.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed partial class RedisRequestDeduplicator(
    IConnectionMultiplexer redis,
    ILogger<RedisRequestDeduplicator> logger
) : IRequestDeduplicator {

    private readonly IConnectionMultiplexer _redis = redis
                                                   ?? throw new ArgumentNullException( nameof( redis ) );
    private readonly ILogger<RedisRequestDeduplicator> _logger = logger
                                                               ?? throw new ArgumentNullException( nameof( logger ) );

    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid( ):N}"[..32];

    private const string InFlightPrefix = "inflight:";
    private const string CompleteChannelPrefix = "complete:";

    /// <inheritdoc/>
    public async Task<DeduplicationResult> TryAcquireAsync(
        string requestKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( requestKey );

        string lockKey = $"{InFlightPrefix}{requestKey}";
        IDatabase db = _redis.GetDatabase( );

        // Try to acquire the lock using SETNX with TTL
        bool acquired = await db.StringSetAsync(
            lockKey,
            _instanceId,
            timeout,
            When.NotExists
        );

        if (acquired) {
            if (_logger.IsEnabled( LogLevel.Debug )) {
                string sanitizedRequestKey = requestKey.SanitizeForLogging( );
                LogLockAcquired( _logger, sanitizedRequestKey, _instanceId );
            }

            return new DeduplicationResult(
                Acquired: true,
                AlreadyInFlight: false,
                RequestKey: requestKey
            );
        }

        // Lock is held by another instance - record deduplication metric
        QueueMetrics.RecordDeduplicated( );

        RedisValue holder = await db.StringGetAsync( lockKey );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            string sanitizedRequestKey = requestKey.SanitizeForLogging( );
            string sanitizedHolder = holder.ToString( ).SanitizeForLogging( );
            LogAlreadyInFlight( _logger, sanitizedRequestKey, sanitizedHolder );
        }

        return new DeduplicationResult(
            Acquired: false,
            AlreadyInFlight: true,
            RequestKey: requestKey
        );
    }

    /// <inheritdoc/>
    public async Task ReleaseAsync(
        string requestKey,
        string? resultUri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( requestKey );

        string lockKey = $"{InFlightPrefix}{requestKey}";
        string channelKey = $"{CompleteChannelPrefix}{requestKey}";

        IDatabase db = _redis.GetDatabase( );
        ISubscriber subscriber = _redis.GetSubscriber( );

        // Verify we own the lock before releasing (prevent accidental release of another instance's lock)
        RedisValue currentHolder = await db.StringGetAsync( lockKey );
        if (currentHolder.HasValue && currentHolder.ToString( ) != _instanceId) {
            LogReleaseDenied(
                _logger,
                requestKey.SanitizeForLogging( ),
                currentHolder.ToString( ).SanitizeForLogging( ),
                _instanceId
            );
            return;
        }

        // Delete the lock
        _ = await db.KeyDeleteAsync( lockKey );

        // Publish completion notification (empty string indicates failure/no result)
        string message = resultUri ?? string.Empty;
        _ = await subscriber.PublishAsync( RedisChannel.Literal( channelKey ), message );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            string sanitizedRequestKey = requestKey.SanitizeForLogging( );
            bool hasResult = !string.IsNullOrEmpty( resultUri );
            LogLockReleased( _logger, sanitizedRequestKey, hasResult );
        }
    }

    /// <inheritdoc/>
    public async Task<string?> WaitForCompletionAsync(
        string requestKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( requestKey );

        string channelKey = $"{CompleteChannelPrefix}{requestKey}";
        ISubscriber subscriber = _redis.GetSubscriber( );

        RedisChannel channel = RedisChannel.Literal( channelKey );

        // Subscribe to completion channel using ChannelMessageQueue for async iteration
        ChannelMessageQueue messageQueue = await subscriber.SubscribeAsync( channel );

        try {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            cts.CancelAfter( timeout );

            // Check if already completed (race condition: completed between TryAcquire and WaitForCompletion)
            IDatabase db = _redis.GetDatabase( );
            string lockKey = $"{InFlightPrefix}{requestKey}";
            bool stillInFlight = await db.KeyExistsAsync( lockKey );

            if (!stillInFlight) {
                // Already completed, the result may have been published before we subscribed
                // Return null to indicate the caller should check cache
                if (_logger.IsEnabled( LogLevel.Debug )) {
                    string sanitizedRequestKey = requestKey.SanitizeForLogging( );
                    LogCompletedBeforeSubscription( _logger, sanitizedRequestKey );
                }
                return null;
            }

            // Wait for a message or timeout
            try {
                await foreach (ChannelMessage message in messageQueue.WithCancellation( cts.Token )) {
                    string result = message.Message.ToString( );
                    return string.IsNullOrEmpty( result ) ? null : result;
                }
                return null;
            } catch (OperationCanceledException) {
                if (_logger.IsEnabled( LogLevel.Debug )) {
                    string sanitizedRequestKey = requestKey.SanitizeForLogging( );
                    LogWaitTimeout( _logger, sanitizedRequestKey );
                }
                return null;
            }
        } finally {
            // Always unsubscribe
            messageQueue.Unsubscribe( );
        }
    }

    /// <summary>
    /// Generates a request key from lookup type and value.
    /// </summary>
    /// <param name="lookupType">The type of lookup.</param>
    /// <param name="lookupValue">The lookup value.</param>
    /// <returns>A request key in the format "{lookupType}:{lookupValue}".</returns>
    public static string GenerateRequestKey( string lookupType, string lookupValue ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupType );
        ArgumentException.ThrowIfNullOrWhiteSpace( lookupValue );

        return $"{lookupType.ToLowerInvariant( )}:{lookupValue.Trim( ).ToUpperInvariant( )}";
    }

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestDeduplicatorLockAcquired,
        Level = LogLevel.Debug,
        Message = "Acquired processing lock for {RequestKey} (instance: {InstanceId})" )]
    internal static partial void LogLockAcquired( ILogger logger, string requestKey, string instanceId );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestDeduplicatorAlreadyInFlight,
        Level = LogLevel.Debug,
        Message = "Request {RequestKey} already in-flight (held by: {Holder})" )]
    internal static partial void LogAlreadyInFlight( ILogger logger, string requestKey, string holder );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestDeduplicatorReleaseDenied,
        Level = LogLevel.Warning,
        Message = "Attempted to release lock for {RequestKey} but held by {Holder}, not {InstanceId}" )]
    internal static partial void LogReleaseDenied( ILogger logger, string requestKey, string holder, string instanceId );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestDeduplicatorLockReleased,
        Level = LogLevel.Debug,
        Message = "Released lock for {RequestKey} and published completion (hasResult: {HasResult})" )]
    internal static partial void LogLockReleased( ILogger logger, string requestKey, bool hasResult );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestDeduplicatorCompletedBeforeSubscription,
        Level = LogLevel.Debug,
        Message = "Request {RequestKey} completed before subscription was active" )]
    internal static partial void LogCompletedBeforeSubscription( ILogger logger, string requestKey );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.RedisRequestDeduplicatorWaitTimeout,
        Level = LogLevel.Debug,
        Message = "Timeout waiting for completion of {RequestKey}" )]
    internal static partial void LogWaitTimeout( ILogger logger, string requestKey );

    #endregion
}
