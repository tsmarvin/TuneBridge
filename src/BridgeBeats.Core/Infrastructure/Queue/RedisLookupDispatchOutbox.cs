using System.Globalization;
using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Redis transactional outbox for initial lookup-provider dispatches. The stage script creates the
/// provider leg and outbox entry atomically. The relay script atomically appends the payload to the
/// provider stream and marks the leg dispatched. Published envelopes remain durable until the saga
/// leg completes so the relay can re-drive a delivery that was acknowledged without completion.
/// </summary>
public sealed partial class RedisLookupDispatchOutbox(
    IConnectionMultiplexer redis,
    IOptions<QueueSettings> settings,
    ILogger<RedisLookupDispatchOutbox> logger
) : ILookupDispatchOutbox {
    private const string PendingSetKey = "outbox:lookup-dispatch:pending";
    private const string PublishedSetKey = "outbox:lookup-dispatch:published";
    private const string DispatchStateField = "dispatchState";
    private const string DispatchPending = "pending";
    private const string DispatchPublished = "published";
    private const string DispatchPublishedAtField = "dispatchPublishedAt";
    private const string OutboxPrefix = "outbox:lookup-dispatch:";
    private static readonly TimeSpan s_redriveAfter = TimeSpan.FromMinutes( 5 );

    private static readonly string s_stageBatchScript = $$"""
        local count = tonumber(ARGV[1])
        if redis.call('exists', KEYS[1]) == 0
            or redis.call('hget', KEYS[1], ARGV[2]) ~= ARGV[3] then
            local mismatches = {}
            for i = 1, count do mismatches[i] = 0 end
            return mismatches
        end

        local outcomes = {}
        local ttl = tonumber(ARGV[7])
        local staleBefore = tonumber(ARGV[8]) - tonumber(ARGV[9])
        for i = 1, count do
            local providerKey = KEYS[2 + (i * 2)]
            local outboxKey = KEYS[3 + (i * 2)]
            local offset = 11 + ((i - 1) * 7)
            local complete = redis.call('hget', providerKey, '{{RedisSagaStateManager.FieldIsComplete}}')
            local dispatchState = redis.call('hget', providerKey, ARGV[4])
            local publishedAt = tonumber(redis.call('hget', providerKey, ARGV[6]) or '')
            local needsDispatch = dispatchState ~= ARGV[5]
                or not publishedAt
                or publishedAt <= staleBefore

            if complete == 'True' or complete == 'true' then
                redis.call('del', outboxKey)
                redis.call('srem', KEYS[2], outboxKey)
                redis.call('srem', KEYS[3], outboxKey)
                outcomes[i] = 2
            elseif needsDispatch then
                if redis.call('exists', providerKey) == 0 then
                    redis.call('hset', providerKey,
                        '{{RedisSagaStateManager.FieldIsComplete}}', 'False',
                        '{{RedisSagaStateManager.FieldIsSuccess}}', 'False',
                        '{{RedisSagaStateManager.FieldResultJson}}', '',
                        '{{RedisSagaStateManager.FieldCompletedAt}}', '',
                        '{{RedisSagaStateManager.FieldErrorMessage}}', '')
                end
                redis.call('hset', providerKey, ARGV[4], ARGV[10])
                redis.call('hdel', providerKey, ARGV[6])
                redis.call('hset', outboxKey,
                    'sagaId', ARGV[offset + 4],
                    'provider', ARGV[offset],
                    'instanceToken', ARGV[3],
                    'payload', ARGV[offset + 1],
                    'targetStream', ARGV[offset + 2],
                    'workChannel', ARGV[offset + 3],
                    'priority', ARGV[offset + 5],
                    'enqueueOrigin', ARGV[offset + 6])
                redis.call('sadd', KEYS[2], outboxKey)
                redis.call('srem', KEYS[3], outboxKey)
                redis.call('expire', outboxKey, ttl)
                outcomes[i] = 1
            else
                outcomes[i] = 2
            end
            redis.call('expire', providerKey, ttl)
        end
        return outcomes
        """;

    private static readonly string s_dispatchScript = $$"""
        if redis.call('exists', KEYS[3]) == 0 then
            redis.call('srem', KEYS[4], KEYS[3])
            redis.call('srem', KEYS[5], KEYS[3])
            return false
        end
        if redis.call('exists', KEYS[1]) == 0 or redis.call('hget', KEYS[1], ARGV[1]) ~= ARGV[2] then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            redis.call('srem', KEYS[5], KEYS[3])
            return false
        end
        local complete = redis.call('hget', KEYS[2], '{{RedisSagaStateManager.FieldIsComplete}}')
        if complete == 'True' or complete == 'true' then
            redis.call('hset', KEYS[2], ARGV[3], ARGV[4])
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            redis.call('srem', KEYS[5], KEYS[3])
            return false
        end
        if redis.call('hget', KEYS[2], ARGV[3]) ~= ARGV[12] then
            return false
        end
        local payload = redis.call('hget', KEYS[3], 'payload')
        if not payload or payload == '' then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            redis.call('srem', KEYS[5], KEYS[3])
            return false
        end
        local messageId = redis.call('xadd', KEYS[6], '*', ARGV[5], payload, ARGV[6], ARGV[7])
        redis.call('hset', KEYS[2], ARGV[3], ARGV[4], ARGV[10], ARGV[11])
        redis.call('expire', KEYS[2], tonumber(ARGV[8]))
        redis.call('expire', KEYS[3], tonumber(ARGV[8]))
        redis.call('srem', KEYS[4], KEYS[3])
        redis.call('sadd', KEYS[5], KEYS[3])
        redis.call('publish', ARGV[9], messageId)
        return messageId
        """;

    private static readonly string s_redriveScript = $$"""
        if redis.call('exists', KEYS[3]) == 0 then
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        if redis.call('exists', KEYS[1]) == 0 or redis.call('hget', KEYS[1], ARGV[1]) ~= ARGV[2] then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        local complete = redis.call('hget', KEYS[2], '{{RedisSagaStateManager.FieldIsComplete}}')
        if complete == 'True' or complete == 'true' then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        if redis.call('hget', KEYS[2], ARGV[3]) ~= ARGV[4] then
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        local publishedAt = tonumber(redis.call('hget', KEYS[2], ARGV[5]) or '')
        if publishedAt and publishedAt > tonumber(ARGV[6]) then
            return false
        end
        local payload = redis.call('hget', KEYS[3], 'payload')
        if not payload or payload == '' then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        local messageId = redis.call('xadd', KEYS[5], '*', ARGV[7], payload, ARGV[8], ARGV[9])
        redis.call('hset', KEYS[2], ARGV[5], ARGV[10])
        redis.call('expire', KEYS[2], tonumber(ARGV[11]))
        redis.call('expire', KEYS[3], tonumber(ARGV[11]))
        redis.call('publish', ARGV[12], messageId)
        return messageId
        """;

    private readonly IConnectionMultiplexer _redis = redis
        ?? throw new ArgumentNullException( nameof( redis ) );
    private readonly QueueSettings _settings = (settings
        ?? throw new ArgumentNullException( nameof( settings ) )).Value;
    private readonly ILogger<RedisLookupDispatchOutbox> _logger = logger
        ?? throw new ArgumentNullException( nameof( logger ) );
    private readonly JsonSerializerOptions _jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <inheritdoc/>
    public async Task<ProviderDispatchStageOutcome> StageAsync(
        QueuedLookupRequest request,
        QueuePriority priority,
        CancellationToken cancellationToken = default
    ) {
        IReadOnlyDictionary<SupportedProviders, ProviderDispatchStageOutcome> outcomes =
            await StageBatchAsync( [request], priority, cancellationToken );
        return outcomes[request.Provider];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<SupportedProviders, ProviderDispatchStageOutcome>> StageBatchAsync(
        IReadOnlyList<QueuedLookupRequest> requests,
        QueuePriority priority,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull( requests );
        if (requests.Count == 0) {
            throw new ArgumentException( "At least one provider dispatch is required.", nameof( requests ) );
        }
        cancellationToken.ThrowIfCancellationRequested( );

        QueuedLookupRequest first = requests[0]
            ?? throw new ArgumentException( "Provider dispatches cannot contain null requests.", nameof( requests ) );
        ArgumentException.ThrowIfNullOrWhiteSpace( first.SagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( first.SagaInstanceToken );
        if (requests.Any( request => request is null
            || !string.Equals( request.SagaId, first.SagaId, StringComparison.Ordinal )
            || !string.Equals( request.SagaInstanceToken, first.SagaInstanceToken, StringComparison.Ordinal ) )) {
            throw new ArgumentException( "Every provider dispatch must belong to the same saga generation.", nameof( requests ) );
        }
        if (requests.Select( request => request.Provider ).Distinct( ).Count( ) != requests.Count) {
            throw new ArgumentException( "Provider dispatches must contain unique providers.", nameof( requests ) );
        }

        long ttlSeconds = (long)TimeSpan.FromMinutes( _settings.JobExpirationMinutes ).TotalSeconds;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<RedisKey> keys = [
            RedisSagaStateManager.GetSagaKey( first.SagaId ),
            PendingSetKey,
            PublishedSetKey
        ];
        List<RedisValue> values = [
            requests.Count,
            RedisSagaStateManager.FieldInstanceToken,
            first.SagaInstanceToken,
            DispatchStateField,
            DispatchPublished,
            DispatchPublishedAtField,
            ttlSeconds,
            now.ToUnixTimeMilliseconds( ),
            (long)s_redriveAfter.TotalMilliseconds,
            DispatchPending
        ];

        foreach (QueuedLookupRequest request in requests) {
            (string targetStream, string workChannel, QueuePriority effectivePriority) =
                ResolveDispatchTarget( request, priority );
            keys.Add( RedisSagaStateManager.GetProviderKey( request.SagaId, request.Provider ) );
            keys.Add( GetOutboxKey( request.SagaId, request.Provider ) );
            values.Add( ((int)request.Provider).ToString( CultureInfo.InvariantCulture ) );
            values.Add( JsonSerializer.Serialize( request, _jsonOptions ) );
            values.Add( targetStream );
            values.Add( workChannel );
            values.Add( request.SagaId );
            values.Add( ((int)effectivePriority).ToString( CultureInfo.InvariantCulture ) );
            values.Add( ((int)request.EnqueueOrigin).ToString( CultureInfo.InvariantCulture ) );
        }

        RedisResult result = await _redis.GetDatabase( ).ScriptEvaluateAsync(
            s_stageBatchScript, [.. keys], [.. values] );
        RedisResult[] rawOutcomes = (RedisResult[])result!;
        if (rawOutcomes.Length != requests.Count) {
            throw new InvalidOperationException( "Redis returned an invalid provider-dispatch outcome count." );
        }

        Dictionary<SupportedProviders, ProviderDispatchStageOutcome> outcomes = [];
        for (int index = 0; index < requests.Count; index++) {
            QueuedLookupRequest request = requests[index];
            ProviderDispatchStageOutcome outcome = (ProviderDispatchStageOutcome)(int)rawOutcomes[index];
            outcomes.Add( request.Provider, outcome );
            if (outcome == ProviderDispatchStageOutcome.Staged) {
                LogDispatchStaged( _logger, request.SagaId, request.Provider );
            }
        }
        return outcomes;
    }

    /// <inheritdoc/>
    public async Task<bool> DispatchAsync(
        string sagaId,
        SupportedProviders provider,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        cancellationToken.ThrowIfCancellationRequested( );
        return await DispatchOutboxKeyAsync( GetOutboxKey( sagaId, provider ), cancellationToken );
    }

    /// <inheritdoc/>
    public async Task<int> DispatchPendingAsync( int limit, CancellationToken cancellationToken = default ) {
        if (limit <= 0) {
            throw new ArgumentOutOfRangeException( nameof( limit ) );
        }
        cancellationToken.ThrowIfCancellationRequested( );

        IDatabase db = _redis.GetDatabase( );
        int dispatched = 0;
        int visited = 0;
        await foreach (RedisValue member in db.SetScanAsync(
            PendingSetKey,
            pageSize: Math.Min( limit, 100 ) )) {
            cancellationToken.ThrowIfCancellationRequested( );
            if (await DispatchOutboxKeyAsync( member.ToString( ), cancellationToken )) {
                dispatched++;
            }
            visited++;
            if (visited >= limit) {
                break;
            }
        }

        if (visited < limit) {
            await foreach (RedisValue member in db.SetScanAsync(
                PublishedSetKey,
                pageSize: Math.Min( limit - visited, 100 ) )) {
                cancellationToken.ThrowIfCancellationRequested( );
                if (await RedriveOutboxKeyAsync( member.ToString( ), cancellationToken )) {
                    dispatched++;
                }
                visited++;
                if (visited >= limit) {
                    break;
                }
            }
        }
        return dispatched;
    }

    private async Task<bool> DispatchOutboxKeyAsync(
        string outboxKey,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested( );
        IDatabase db = _redis.GetDatabase( );
        DispatchMetadata? metadata = await ReadDispatchMetadataAsync( db, outboxKey );
        if (metadata is null) {
            return false;
        }

        long ttlSeconds = (long)TimeSpan.FromMinutes( _settings.JobExpirationMinutes ).TotalSeconds;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        RedisResult result = await db.ScriptEvaluateAsync(
            s_dispatchScript,
            [
                RedisSagaStateManager.GetSagaKey( metadata.SagaId ),
                RedisSagaStateManager.GetProviderKey( metadata.SagaId, metadata.Provider ),
                outboxKey,
                PendingSetKey,
                PublishedSetKey,
                metadata.TargetStream
            ],
            [
                "instanceToken",
                metadata.InstanceToken,
                DispatchStateField,
                DispatchPublished,
                QueueStreamFieldNames.Payload,
                QueueStreamFieldNames.EnqueuedAt,
                now.ToString( "O", CultureInfo.InvariantCulture ),
                ttlSeconds,
                metadata.WorkChannel,
                DispatchPublishedAtField,
                now.ToUnixTimeMilliseconds( ),
                DispatchPending
            ] );
        RedisValue messageId = (RedisValue)result;
        if (!messageId.HasValue) {
            return false;
        }

        QueueMetrics.RecordEnqueue( metadata.Provider, metadata.Priority, metadata.EnqueueOrigin );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            string publishedMessageId = messageId.ToString( );
            LogDispatchPublished( _logger, metadata.SagaId, metadata.Provider, publishedMessageId );
        }
        return true;
    }

    private async Task<bool> RedriveOutboxKeyAsync(
        string outboxKey,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested( );
        IDatabase db = _redis.GetDatabase( );
        DispatchMetadata? metadata = await ReadDispatchMetadataAsync( db, outboxKey );
        if (metadata is null) {
            return false;
        }

        long ttlSeconds = (long)TimeSpan.FromMinutes( _settings.JobExpirationMinutes ).TotalSeconds;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RedisResult result = await db.ScriptEvaluateAsync(
            s_redriveScript,
            [
                RedisSagaStateManager.GetSagaKey( metadata.SagaId ),
                RedisSagaStateManager.GetProviderKey( metadata.SagaId, metadata.Provider ),
                outboxKey,
                PublishedSetKey,
                metadata.TargetStream
            ],
            [
                RedisSagaStateManager.FieldInstanceToken,
                metadata.InstanceToken,
                DispatchStateField,
                DispatchPublished,
                DispatchPublishedAtField,
                now.Subtract( s_redriveAfter ).ToUnixTimeMilliseconds( ),
                QueueStreamFieldNames.Payload,
                QueueStreamFieldNames.EnqueuedAt,
                now.ToString( "O", CultureInfo.InvariantCulture ),
                now.ToUnixTimeMilliseconds( ),
                ttlSeconds,
                metadata.WorkChannel
            ] );
        RedisValue messageId = (RedisValue)result;
        if (!messageId.HasValue) {
            return false;
        }

        QueueMetrics.RecordEnqueue(
            metadata.Provider,
            metadata.Priority,
            QueueEnqueueOrigin.Requeue );
        if (_logger.IsEnabled( LogLevel.Debug )) {
            string publishedMessageId = messageId.ToString( );
            LogDispatchPublished(
                _logger, metadata.SagaId, metadata.Provider, publishedMessageId );
        }
        return true;
    }

    private static async Task<DispatchMetadata?> ReadDispatchMetadataAsync(
        IDatabase db,
        string outboxKey
    ) {
        RedisValue[] values = await db.HashGetAsync(
            outboxKey,
            ["sagaId", "provider", "instanceToken", "targetStream", "workChannel", "priority", "enqueueOrigin"] );
        if (values.Length == 7
            && values.All( value => !value.IsNullOrEmpty )
            && int.TryParse( values[1].ToString( ), CultureInfo.InvariantCulture, out int providerValue )
            && Enum.IsDefined( (SupportedProviders)providerValue )
            && int.TryParse( values[5].ToString( ), CultureInfo.InvariantCulture, out int priorityValue )
            && Enum.IsDefined( (QueuePriority)priorityValue )
            && int.TryParse( values[6].ToString( ), CultureInfo.InvariantCulture, out int originValue )
            && Enum.IsDefined( (QueueEnqueueOrigin)originValue )) {
            return new DispatchMetadata(
                values[0].ToString( ),
                (SupportedProviders)providerValue,
                values[2].ToString( ),
                values[3].ToString( ),
                values[4].ToString( ),
                (QueuePriority)priorityValue,
                (QueueEnqueueOrigin)originValue );
        }

        _ = await db.KeyDeleteAsync( outboxKey );
        _ = await db.SetRemoveAsync( PendingSetKey, outboxKey );
        _ = await db.SetRemoveAsync( PublishedSetKey, outboxKey );
        return null;
    }

    private sealed record DispatchMetadata(
        string SagaId,
        SupportedProviders Provider,
        string InstanceToken,
        string TargetStream,
        string WorkChannel,
        QueuePriority Priority,
        QueueEnqueueOrigin EnqueueOrigin );

    private static string GetOutboxKey( string sagaId, SupportedProviders provider ) =>
        $"{OutboxPrefix}{sagaId}:{provider}";

    private static (string Stream, string WorkChannel, QueuePriority EffectivePriority)
        ResolveDispatchTarget( QueuedLookupRequest request, QueuePriority priority ) {
        if (request.Provider == SupportedProviders.Spotify
            && priority != QueuePriority.Interactive
            && !request.BypassBulkRouting
            && request.LookupType is LookupRequestType.SongIdLookup or LookupRequestType.AlbumIdLookup) {
            return (
                QueueStreamKeys.SpotifyBulkFor(
                    isTrack: request.LookupType == LookupRequestType.SongIdLookup ),
                QueueStreamKeys.SpotifyBulkWorkSignal( ),
                QueuePriority.Bulk );
        }

        return (
            QueueStreamKeys.For( request.Provider, priority ),
            QueueStreamKeys.WorkSignalFor( request.Provider ),
            priority );
    }

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.LookupDispatchOutboxStaged,
        Level = LogLevel.Debug,
        Message = "Staged lookup dispatch outbox item for saga {SagaId}, provider {Provider}" )]
    private static partial void LogDispatchStaged(
        ILogger logger,
        string sagaId,
        SupportedProviders provider );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.LookupDispatchOutboxPublished,
        Level = LogLevel.Debug,
        Message = "Published lookup dispatch outbox item for saga {SagaId}, provider {Provider}, message {MessageId}" )]
    private static partial void LogDispatchPublished(
        ILogger logger,
        string sagaId,
        SupportedProviders provider,
        string messageId );
}
