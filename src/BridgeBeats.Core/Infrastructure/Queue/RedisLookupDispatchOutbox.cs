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
/// provider stream, marks the leg dispatched, and removes the outbox entry.
/// </summary>
public sealed partial class RedisLookupDispatchOutbox(
    IConnectionMultiplexer redis,
    IOptions<QueueSettings> settings,
    ILogger<RedisLookupDispatchOutbox> logger
) : ILookupDispatchOutbox {
    private const string PendingSetKey = "outbox:lookup-dispatch:pending";
    private const string DispatchStateField = "dispatchState";
    private const string DispatchPending = "pending";
    private const string DispatchPublished = "published";
    private const string SagaPrefix = "saga:";
    private const string ProviderSuffix = ":provider:";
    private const string OutboxPrefix = "outbox:lookup-dispatch:";

    private const string StageScript = """
        if redis.call('exists', KEYS[1]) == 0 or redis.call('hget', KEYS[1], ARGV[1]) ~= ARGV[2] then
            return 0
        end
        local providerExists = redis.call('exists', KEYS[2]) == 1
        local dispatchState = redis.call('hget', KEYS[2], ARGV[3])
        if dispatchState == ARGV[4] then
            redis.call('expire', KEYS[2], tonumber(ARGV[5]))
            return 2
        end
        if providerExists and not dispatchState then
            -- Provider legs created before the outbox contract already have a queue delivery.
            redis.call('hset', KEYS[2], ARGV[3], ARGV[4])
            redis.call('expire', KEYS[2], tonumber(ARGV[5]))
            return 2
        end
        if not providerExists then
            redis.call('hset', KEYS[2],
                'isComplete', 'False',
                'isSuccess', 'False',
                'resultJson', '',
                'completedAt', '',
                'errorMessage', '')
        end
        redis.call('hset', KEYS[2], ARGV[3], ARGV[6])
        if redis.call('exists', KEYS[3]) == 0 then
            redis.call('hset', KEYS[3],
                'sagaId', ARGV[7],
                'provider', ARGV[8],
                'instanceToken', ARGV[2],
                'payload', ARGV[9],
                'targetStream', ARGV[10],
                'workChannel', ARGV[11])
        end
        redis.call('sadd', KEYS[4], KEYS[3])
        redis.call('expire', KEYS[2], tonumber(ARGV[5]))
        redis.call('expire', KEYS[3], tonumber(ARGV[5]))
        return 1
        """;

    private const string DispatchScript = """
        if redis.call('exists', KEYS[3]) == 0 then
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        if redis.call('exists', KEYS[1]) == 0 or redis.call('hget', KEYS[1], ARGV[1]) ~= ARGV[2] then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        local complete = redis.call('hget', KEYS[2], 'isComplete')
        if complete == 'True' or complete == 'true' then
            redis.call('hset', KEYS[2], ARGV[3], ARGV[4])
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        local payload = redis.call('hget', KEYS[3], 'payload')
        if not payload or payload == '' then
            redis.call('del', KEYS[3])
            redis.call('srem', KEYS[4], KEYS[3])
            return false
        end
        local messageId = redis.call('xadd', KEYS[5], '*', ARGV[5], payload, ARGV[6], ARGV[7])
        redis.call('hset', KEYS[2], ARGV[3], ARGV[4])
        redis.call('expire', KEYS[2], tonumber(ARGV[8]))
        redis.call('del', KEYS[3])
        redis.call('srem', KEYS[4], KEYS[3])
        redis.call('publish', ARGV[9], messageId)
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
        ArgumentNullException.ThrowIfNull( request );
        ArgumentException.ThrowIfNullOrWhiteSpace( request.SagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( request.SagaInstanceToken );
        cancellationToken.ThrowIfCancellationRequested( );

        string sagaKey = GetSagaKey( request.SagaId );
        string providerKey = GetProviderKey( request.SagaId, request.Provider );
        string outboxKey = GetOutboxKey( request.SagaId, request.Provider );
        string targetStream = QueueStreamKeys.For( request.Provider, priority );
        string workChannel = QueueStreamKeys.WorkSignalFor( request.Provider );
        long ttlSeconds = (long)TimeSpan.FromMinutes( _settings.JobExpirationMinutes ).TotalSeconds;
        string payload = JsonSerializer.Serialize( request, _jsonOptions );

        RedisResult result = await _redis.GetDatabase( ).ScriptEvaluateAsync(
            StageScript,
            [sagaKey, providerKey, outboxKey, PendingSetKey],
            [
                "instanceToken",
                request.SagaInstanceToken,
                DispatchStateField,
                DispatchPublished,
                ttlSeconds,
                DispatchPending,
                request.SagaId,
                ((int)request.Provider).ToString( CultureInfo.InvariantCulture ),
                payload,
                targetStream,
                workChannel
            ] );

        ProviderDispatchStageOutcome outcome = (ProviderDispatchStageOutcome)(int)result;
        if (outcome == ProviderDispatchStageOutcome.Staged) {
            LogDispatchStaged( _logger, request.SagaId, request.Provider );
        }
        return outcome;
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
        return dispatched;
    }

    private async Task<bool> DispatchOutboxKeyAsync(
        string outboxKey,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested( );
        IDatabase db = _redis.GetDatabase( );
        RedisValue[] values = await db.HashGetAsync(
            outboxKey,
            ["sagaId", "provider", "instanceToken", "targetStream", "workChannel"] );
        if (values.Length != 5
            || values.Any( value => value.IsNullOrEmpty )
            || !int.TryParse( values[1].ToString( ), CultureInfo.InvariantCulture, out int providerValue )
            || !Enum.IsDefined( (SupportedProviders)providerValue )) {
            _ = await db.SetRemoveAsync( PendingSetKey, outboxKey );
            return false;
        }

        string sagaId = values[0].ToString( );
        SupportedProviders provider = (SupportedProviders)providerValue;
        string instanceToken = values[2].ToString( );
        string targetStream = values[3].ToString( );
        string workChannel = values[4].ToString( );
        long ttlSeconds = (long)TimeSpan.FromMinutes( _settings.JobExpirationMinutes ).TotalSeconds;

        RedisResult result = await db.ScriptEvaluateAsync(
            DispatchScript,
            [GetSagaKey( sagaId ), GetProviderKey( sagaId, provider ), outboxKey, PendingSetKey, targetStream],
            [
                "instanceToken",
                instanceToken,
                DispatchStateField,
                DispatchPublished,
                QueueStreamFieldNames.Payload,
                QueueStreamFieldNames.EnqueuedAt,
                DateTimeOffset.UtcNow.ToString( "O", CultureInfo.InvariantCulture ),
                ttlSeconds,
                workChannel
            ] );
        RedisValue messageId = (RedisValue)result;
        if (!messageId.HasValue) {
            return false;
        }

        if (_logger.IsEnabled( LogLevel.Debug )) {
            string publishedMessageId = messageId.ToString( );
            LogDispatchPublished( _logger, sagaId, provider, publishedMessageId );
        }
        return true;
    }

    private static string GetSagaKey( string sagaId ) => $"{SagaPrefix}{sagaId}";
    private static string GetProviderKey( string sagaId, SupportedProviders provider ) =>
        $"{SagaPrefix}{sagaId}{ProviderSuffix}{provider}";
    private static string GetOutboxKey( string sagaId, SupportedProviders provider ) =>
        $"{OutboxPrefix}{sagaId}:{provider}";

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
