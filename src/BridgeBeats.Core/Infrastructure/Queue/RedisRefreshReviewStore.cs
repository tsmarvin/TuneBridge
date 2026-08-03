using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// Redis-backed refresh review store. Pending saga context is short-lived; unresolved review
/// entries remain until an operator explicitly removes them.
/// </summary>
public sealed partial class RedisRefreshReviewStore(
    IConnectionMultiplexer redis,
    IOptions<QueueSettings> settings,
    ILogger<RedisRefreshReviewStore> logger
) : IRefreshReviewStore {
    private const string PendingPrefix = "cache:refresh:pending:";
    private const string UnresolvedKey = "cache:refresh:unresolved";
    private const string UnresolvedSagaIndexKey = "cache:refresh:unresolved:sagas";
    private const string CorruptKey = "cache:refresh:unresolved:corrupt";
    private const string DeleteUnresolvedScript = """
        local json = redis.call('HGET', KEYS[1], ARGV[1])
        if not json then
            return 0
        end
        local ok, entry = pcall(cjson.decode, json)
        if not ok then
            return -1
        end
        if entry['sagaId'] ~= ARGV[2] or entry['sourceRecordCid'] ~= ARGV[3] then
            return 0
        end
        redis.call('HDEL', KEYS[1], ARGV[1])
        redis.call('HDEL', KEYS[2], ARGV[2])
        return 1
        """;

    private static readonly JsonSerializerOptions s_jsonOptions = new( JsonSerializerDefaults.Web );
    private readonly IConnectionMultiplexer _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
    private readonly ILogger<RedisRefreshReviewStore> _logger = logger
        ?? throw new ArgumentNullException( nameof( logger ) );
    private readonly TimeSpan _pendingTtl = TimeSpan.FromMinutes(
        settings?.Value.JobExpirationMinutes ?? throw new ArgumentNullException( nameof( settings ) )
    );

    /// <inheritdoc/>
    public async Task RegisterPendingAsync( RefreshReviewEntry entry, CancellationToken cancellationToken = default ) {
        ArgumentNullException.ThrowIfNull( entry );
        cancellationToken.ThrowIfCancellationRequested( );

        string json = JsonSerializer.Serialize( entry, s_jsonOptions );
        _ = await _redis.GetDatabase( ).StringSetAsync( PendingPrefix + entry.SagaId, json, _pendingTtl );
    }

    /// <inheritdoc/>
    public async Task MarkUnresolvedAsync( string sagaId, string reason, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( reason );
        cancellationToken.ThrowIfCancellationRequested( );

        IDatabase db = _redis.GetDatabase( );
        RedisValue pendingJson = await db.StringGetAsync( PendingPrefix + sagaId );
        if (pendingJson.IsNullOrEmpty) {
            return;
        }

        RefreshReviewEntry? pending = JsonSerializer.Deserialize<RefreshReviewEntry>( pendingJson.ToString( ), s_jsonOptions );
        if (pending is null) {
            return;
        }

        RefreshReviewEntry unresolved = pending with {
            FailedAt = DateTimeOffset.UtcNow,
            FailureReason = reason
        };
        string unresolvedJson = JsonSerializer.Serialize( unresolved, s_jsonOptions );

        RedisValue previousJson = await db.HashGetAsync( UnresolvedKey, unresolved.SourceRecordUri );
        RefreshReviewEntry? previous = TryDeserialize( previousJson );
        if (previous is not null && !string.Equals( previous.SagaId, sagaId, StringComparison.Ordinal )) {
            _ = await db.HashDeleteAsync( UnresolvedSagaIndexKey, previous.SagaId );
        }
        _ = await db.HashSetAsync( UnresolvedKey, unresolved.SourceRecordUri, unresolvedJson );
        _ = await db.HashSetAsync( UnresolvedSagaIndexKey, unresolved.SagaId, unresolved.SourceRecordUri );
        _ = await db.KeyDeleteAsync( PendingPrefix + sagaId );
    }

    /// <inheritdoc/>
    public async Task CompleteAsync( string sagaId, CancellationToken cancellationToken = default ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sagaId );
        cancellationToken.ThrowIfCancellationRequested( );
        IDatabase db = _redis.GetDatabase( );
        RedisValue pendingJson = await db.StringGetAsync( PendingPrefix + sagaId );
        RefreshReviewEntry? pending = TryDeserialize( pendingJson );
        RedisValue indexedSourceRecordUri = await db.HashGetAsync( UnresolvedSagaIndexKey, sagaId );
        string? sourceRecordUri = pending?.SourceRecordUri;
        bool matchedByPendingContext = sourceRecordUri is not null;
        sourceRecordUri ??= indexedSourceRecordUri.IsNullOrEmpty
            ? null
            : indexedSourceRecordUri.ToString( );

        if (sourceRecordUri is not null) {
            RedisValue currentJson = await db.HashGetAsync( UnresolvedKey, sourceRecordUri );
            RefreshReviewEntry? current = TryDeserialize( currentJson );

            // A successful actively-pending refresh supersedes any older review for the same
            // source URI. Without pending context, an old saga index may only clear its own entry.
            if (matchedByPendingContext
                || string.Equals( current?.SagaId, sagaId, StringComparison.Ordinal )) {
                _ = await db.HashDeleteAsync( UnresolvedKey, sourceRecordUri );
                if (current is not null
                    && !string.Equals( current.SagaId, sagaId, StringComparison.Ordinal )) {
                    _ = await db.HashDeleteAsync( UnresolvedSagaIndexKey, current.SagaId );
                }
            }
        }
        _ = await db.HashDeleteAsync( UnresolvedSagaIndexKey, sagaId );
        _ = await db.KeyDeleteAsync( PendingPrefix + sagaId );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RefreshReviewEntry>> GetUnresolvedAsync( CancellationToken cancellationToken = default ) {
        cancellationToken.ThrowIfCancellationRequested( );
        HashEntry[] entries = await _redis.GetDatabase( ).HashGetAllAsync( UnresolvedKey );

        List<RefreshReviewEntry> results = [];
        foreach (HashEntry entry in entries) {
            try {
                RefreshReviewEntry? result = JsonSerializer.Deserialize<RefreshReviewEntry>( entry.Value.ToString( ), s_jsonOptions );
                if (result is not null) {
                    results.Add( result );
                }
            } catch (JsonException ex) {
                LogCorruptEntryQuarantined( _logger, ex, entry.Name.ToString( ) );
                _ = await _redis.GetDatabase( ).HashSetAsync( CorruptKey, entry.Name, entry.Value );
                _ = await _redis.GetDatabase( ).HashDeleteAsync( UnresolvedKey, entry.Name );
            }
        }

        return results
            .OrderByDescending( entry => entry.FailedAt )
            .ThenBy( entry => entry.SourceRecordUri, StringComparer.Ordinal )
            .ToArray( );
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteUnresolvedAsync(
        string sourceRecordUri,
        string expectedSagaId,
        string expectedSourceRecordCid,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sourceRecordUri );
        ArgumentException.ThrowIfNullOrWhiteSpace( expectedSagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( expectedSourceRecordCid );
        cancellationToken.ThrowIfCancellationRequested( );

        RedisResult result = await _redis.GetDatabase( ).ScriptEvaluateAsync(
            DeleteUnresolvedScript,
            keys: [UnresolvedKey, UnresolvedSagaIndexKey],
            values: [sourceRecordUri, expectedSagaId, expectedSourceRecordCid] );
        long outcome = (long)result;
        if (outcome < 0) {
            throw new InvalidOperationException(
                $"Refresh-review entry for '{sourceRecordUri}' is corrupt and could not be revision-checked." );
        }
        return outcome == 1;
    }

    private static RefreshReviewEntry? TryDeserialize( RedisValue json ) {
        if (json.IsNullOrEmpty) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<RefreshReviewEntry>( json.ToString( ), s_jsonOptions );
        } catch (JsonException) {
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Quarantined corrupt refresh-review entry {SourceRecordUri}" )]
    private static partial void LogCorruptEntryQuarantined(
        ILogger logger,
        Exception ex,
        string sourceRecordUri );
}
