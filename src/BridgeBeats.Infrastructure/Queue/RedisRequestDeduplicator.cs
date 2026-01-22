using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Queue;

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
public sealed class RedisRequestDeduplicator : IRequestDeduplicator {

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRequestDeduplicator> _logger;
    private readonly string _instanceId;

    private const string InFlightPrefix = "inflight:";
    private const string CompleteChannelPrefix = "complete:";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisRequestDeduplicator"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public RedisRequestDeduplicator(
        IConnectionMultiplexer redis,
        ILogger<RedisRequestDeduplicator> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _instanceId = $"{Environment.MachineName}:{Guid.NewGuid( ):N}"[..32];
    }

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
            _logger.LogDebug(
                "Acquired processing lock for {RequestKey} (instance: {InstanceId})",
                requestKey.SanitizeForLogging( ),
                _instanceId
            );

            return new DeduplicationResult(
                Acquired: true,
                AlreadyInFlight: false,
                RequestKey: requestKey
            );
        }

        // Lock is held by another instance - record deduplication metric
        QueueMetrics.RecordDeduplicated( );

        RedisValue holder = await db.StringGetAsync( lockKey );

        _logger.LogDebug(
            "Request {RequestKey} already in-flight (held by: {Holder})",
            requestKey.SanitizeForLogging( ),
            holder.ToString( ).SanitizeForLogging( )
        );

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
            _logger.LogWarning(
                "Attempted to release lock for {RequestKey} but held by {Holder}, not {InstanceId}",
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

        _logger.LogDebug(
            "Released lock for {RequestKey} and published completion (hasResult: {HasResult})",
            requestKey.SanitizeForLogging( ),
            !string.IsNullOrEmpty( resultUri )
        );
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
                _logger.LogDebug(
                    "Request {RequestKey} completed before subscription was active",
                    requestKey.SanitizeForLogging( )
                );
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
                _logger.LogDebug(
                    "Timeout waiting for completion of {RequestKey}",
                    requestKey.SanitizeForLogging( )
                );
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
}
