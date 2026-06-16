using System.Text.Json;
using System.Threading.Channels;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Storage;
using StackExchange.Redis;

namespace BridgeBeats.Core.Domain.Services;

/// <summary>
/// Computes and caches aggregate lookup statistics from the ATProto record store: total records,
/// album/track split, per-provider counts, the most recent entries, earliest/latest lookup times,
/// and the cache-bootstrap status read from Redis. A single in-memory snapshot is served to callers
/// and refreshed either on a cache-expiry schedule or on demand.
/// </summary>
/// <remarks>
/// Refreshes are serialized by a semaphore and gated by cache expiry, so only one computation runs
/// at a time and a fresh cache is reused unless a force refresh is requested. Manual refreshes are
/// requested through a bounded, drop-newest channel that coalesces bursts into a single run.
/// </remarks>
/// <param name="atProtoStorage">The ATProto record store streamed to compute statistics.</param>
/// <param name="redis">The Redis connection used to read cache-bootstrap status.</param>
/// <param name="settings">Statistics configuration: PDS, user DID, cache duration, startup delay.</param>
/// <param name="logger">The logger for refresh lifecycle and Redis read failures.</param>
public sealed partial class StatisticsService(
    IATProtoStorageService atProtoStorage,
    IConnectionMultiplexer redis,
    StatisticsSettings settings,
    ILogger<StatisticsService> logger
) : IStatisticsService {

    /// <summary>Case-insensitive JSON options used to deserialize the Redis cache-bootstrap status.</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) { PropertyNameCaseInsensitive = true };
    /// <summary>Serializes refreshes so only one statistics computation runs at a time.</summary>
    private readonly SemaphoreSlim _cacheLock = new( 1, 1 );
    /// <summary>The most recently computed statistics snapshot served to callers, or null before the first run.</summary>
    private LookupStatistics? _cachedStats;
    /// <summary>The time at which the cached snapshot becomes stale and eligible for a non-forced refresh.</summary>
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    /// <summary>Flag (0/1) indicating whether a refresh is currently in progress; see <see cref="IsRefreshing"/>.</summary>
    private volatile int _isRefreshing;

    /// <summary>
    /// Bounded, single-slot channel used to request manual refreshes. The drop-newest policy
    /// coalesces a burst of triggers into at most one pending refresh.
    /// </summary>
    private readonly Channel<bool> _refreshChannel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropNewest }
    );

    /// <summary>
    /// Gets the reader side of the manual-refresh trigger channel. The background refresh service
    /// awaits this to coalesce and drive on-demand refreshes.
    /// </summary>
    public ChannelReader<bool> RefreshTriggerReader => _refreshChannel.Reader;

    /// <summary>Gets a value indicating whether a statistics refresh is currently in progress.</summary>
    public bool IsRefreshing => Interlocked.CompareExchange( ref _isRefreshing, 0, 0 ) == 1;

    /// <summary>Gets the last computed statistics snapshot without triggering a refresh, or null if none has been computed yet.</summary>
    /// <returns>The cached snapshot, or <see langword="null"/> when no refresh has completed.</returns>
    public LookupStatistics? GetCachedStatistics( ) {
        return _cachedStats;
    }

    /// <summary>
    /// Requests a manual refresh by writing to the trigger channel. No-op (returns
    /// <see langword="false"/>) when a refresh is already running or a trigger is already pending.
    /// </summary>
    /// <returns><see langword="true"/> if a refresh was queued; otherwise <see langword="false"/>.</returns>
    public bool TriggerRefresh( ) {
        return !IsRefreshing && _refreshChannel.Writer.TryWrite( true );
    }

    /// <summary>
    /// Returns the current statistics snapshot without recomputing. If no snapshot exists yet,
    /// returns an empty <see cref="LookupStatistics"/> with a minimal <c>GeneratedAt</c>.
    /// </summary>
    /// <param name="cancellationToken">Unused; present to satisfy the interface.</param>
    /// <returns>The cached snapshot, or an empty placeholder when none exists.</returns>
    public Task<LookupStatistics> GetStatisticsAsync( CancellationToken cancellationToken = default ) {
        LookupStatistics stats = _cachedStats ?? new LookupStatistics { GeneratedAt = DateTimeOffset.MinValue };
        return Task.FromResult( stats );
    }

    /// <summary>
    /// Refreshes the statistics snapshot, reusing a fresh cache when present. Equivalent to calling
    /// the force-aware overload with <c>forceRefresh: false</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>The refreshed (or still-fresh cached) statistics snapshot.</returns>
    public async Task<LookupStatistics> RefreshStatisticsAsync( CancellationToken cancellationToken = default ) {
        return await RefreshStatisticsAsync( false, cancellationToken );
    }

    /// <summary>
    /// Reads the live cache-bootstrap status directly from Redis, bypassing the cached snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The current bootstrap status, or <see langword="null"/> when absent or unreadable.</returns>
    public async Task<CacheBootstrapStatus?> GetLiveBootstrapStatusAsync( CancellationToken cancellationToken = default ) =>
        await GetCacheBootstrapStatusAsync( ).WaitAsync( cancellationToken );

    /// <summary>
    /// Refreshes the statistics snapshot under the refresh lock. When <paramref name="forceRefresh"/>
    /// is <see langword="false"/> and the cache is still fresh, the cached snapshot is returned
    /// without recomputation. Otherwise the statistics are recomputed, cached, and the cache expiry
    /// is reset to now plus the configured cache duration.
    /// </summary>
    /// <param name="forceRefresh">When <see langword="true"/>, recomputes even if the cache is still fresh.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>The refreshed (or still-fresh cached) statistics snapshot.</returns>
    public async Task<LookupStatistics> RefreshStatisticsAsync( bool forceRefresh, CancellationToken cancellationToken = default ) {
        await _cacheLock.WaitAsync( cancellationToken );
        try {
            // Double-check: if cache was recently refreshed by another thread, skip
            if (!forceRefresh && _cachedStats is not null && DateTimeOffset.UtcNow < _cacheExpiry) {
                LogStatisticsRefreshSkipped( logger );
                return _cachedStats;
            }

            _ = Interlocked.Exchange( ref _isRefreshing, 1 );
            try {
                if (logger.IsEnabled( LogLevel.Information )) {
                    string pdsUriString = settings.PdsUri.ToString();
                    LogRefreshingStatistics( logger, pdsUriString, settings.UserDid );
                }

                DateTimeOffset refreshStart = DateTimeOffset.UtcNow;
                LookupStatistics stats = await ComputeStatisticsAsync(cancellationToken);

                _cachedStats = stats;
                _cacheExpiry = refreshStart + settings.CacheDuration;

                LogStatisticsRefreshed( logger, stats.TotalRecords, _cacheExpiry );

                return stats;
            } finally {
                _ = Interlocked.Exchange( ref _isRefreshing, 0 );
            }
        } finally {
            _ = _cacheLock.Release( );
        }
    }

    /// <summary>
    /// Streams every record from the ATProto store and aggregates them into a
    /// <see cref="LookupStatistics"/>: total count, album/track split, per-provider counts (sorted
    /// descending), earliest/latest lookup times, and the most recent entries. A bounded buffer is
    /// kept while streaming and trimmed to the top entries by lookup time, so memory stays bounded
    /// regardless of record count. The Redis cache-bootstrap status is attached at the end.
    /// </summary>
    /// <param name="cancellationToken">Cancels the streaming aggregation.</param>
    /// <returns>The freshly computed statistics snapshot.</returns>
    private async Task<LookupStatistics> ComputeStatisticsAsync( CancellationToken cancellationToken ) {
        int totalCount = 0;
        int albumCount = 0;
        int trackCount = 0;
        Dictionary<string, int> providerCounts = [];
        List<(string AtUri, MediaLinkResult Result, DateTimeOffset LookedUpAt)> recentCandidates = [];
        DateTimeOffset? earliestLookup = null;
        DateTimeOffset? latestLookup = null;

        await foreach ((string atUri, MediaLinkResult result) in
            atProtoStorage.ListAllRecordsAsync( settings.PdsUri, settings.UserDid, cancellationToken )) {
            totalCount++;

            // Determine if album or track from first result
            bool isAlbum = result.Results.Values.FirstOrDefault( )?.IsAlbum ?? false;
            if (isAlbum) {
                albumCount++;
            } else {
                trackCount++;
            }

            // Count by provider
            foreach (SupportedProviders provider in result.Results.Keys) {
                string providerName = provider.ToString( );
                providerCounts[providerName] = providerCounts.GetValueOrDefault( providerName ) + 1;
            }

            // Track timestamps for date range
            DateTimeOffset lookedUpAt = result.LookedUpAt;
            if (earliestLookup is null || lookedUpAt < earliestLookup) {
                earliestLookup = lookedUpAt;
            }
            if (latestLookup is null || lookedUpAt > latestLookup) {
                latestLookup = lookedUpAt;
            }

            // Track recent entries (keep top 5)
            recentCandidates.Add( (atUri, result, lookedUpAt) );
            if (recentCandidates.Count > 100) {
                // Trim to keep memory usage reasonable during iteration
                recentCandidates = [.. recentCandidates
                    .OrderByDescending( x => x.LookedUpAt )
                    .Take( 10 )];
            }
        }

        // Build recent entries list
        List<RecentLookupEntry> recentEntries = [..
            recentCandidates
            .OrderByDescending( x => x.LookedUpAt )
            .Take( 5 )
            .Select( x => CreateRecentEntry( x.AtUri, x.Result ) )
        ];

        // Fetch cache bootstrap status from Redis
        CacheBootstrapStatus? bootstrapStatus = await GetCacheBootstrapStatusAsync( );

        return new LookupStatistics {
            TotalRecords = totalCount,
            AlbumCount = albumCount,
            TrackCount = trackCount,
            ProviderCounts = providerCounts.OrderByDescending( x => x.Value ).ToDictionary( x => x.Key, x => x.Value ),
            RecentEntries = recentEntries,
            EarliestLookup = earliestLookup,
            LatestLookup = latestLookup,
            GeneratedAt = DateTimeOffset.UtcNow,
            CacheBootstrapStatus = bootstrapStatus
        };
    }

    /// <summary>
    /// Reads and deserializes the cache-bootstrap status from its well-known Redis key. Returns
    /// <see langword="null"/> when the key is absent; failures are logged and swallowed so a Redis
    /// hiccup does not fail a statistics refresh.
    /// </summary>
    /// <returns>The deserialized bootstrap status, or <see langword="null"/> when absent or on error.</returns>
    private async Task<CacheBootstrapStatus?> GetCacheBootstrapStatusAsync( ) {
        try {
            IDatabase db = redis.GetDatabase( );
            RedisValue value = await db.StringGetAsync( CacheBootstrapStatus.RedisKey );

            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CacheBootstrapStatus>( value.ToString( ), s_jsonOptions );
        } catch (Exception ex) {
            LogCacheBootstrapStatusError( logger, ex );
            return null;
        }
    }

    /// <summary>
    /// Projects a stored record into a <see cref="RecentLookupEntry"/> for the "recent lookups"
    /// list, taking artist/title/album from the first provider result and deriving a card id from
    /// the record key embedded in the AT URI when the URI has the expected shape.
    /// </summary>
    /// <param name="atUri">The record's AT URI; its last segment is the record key.</param>
    /// <param name="result">The stored multi-provider result.</param>
    /// <returns>A recent-lookup entry summarizing the record.</returns>
    private static RecentLookupEntry CreateRecentEntry( string atUri, MediaLinkResult result ) {
        MusicLookupResult? firstResult = result.Results.Values.FirstOrDefault( );
        string? cardId = null;

        // Extract rkey from AT-URI and generate card ID
        // Format: at://did:plc:xxx/link.bridgebeats.lookup/rkey
        string[] uriParts = atUri.Split( '/' );
        if (uriParts.Length >= 5) {
            string rkey = uriParts[^1];
            cardId = RecordKeyGenerator.GenerateCardId( rkey );
        }

        return new RecentLookupEntry {
            AtUri = atUri,
            IsAlbum = firstResult?.IsAlbum ?? false,
            Artist = firstResult?.Artist ?? "Unknown",
            Title = firstResult?.Title ?? "Unknown",
            LookedUpAt = result.LookedUpAt,
            CardId = cardId
        };
    }

    #region LoggerMessage Definitions

    /// <summary>Logs (Information) that a statistics refresh is starting, naming the PDS and user DID.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="pdsUri">The PDS endpoint being read.</param>
    /// <param name="userDid">The user DID whose records are aggregated.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.RefreshingStatistics,
        Level = LogLevel.Information,
        Message = "Refreshing statistics from ATProto PDS: {PdsUri}, DID: {UserDid}" )]
    private static partial void LogRefreshingStatistics( ILogger logger, string pdsUri, string userDid );

    /// <summary>Logs (Information) that a refresh completed, with the record total and new cache expiry.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="totalRecords">The total number of records aggregated.</param>
    /// <param name="cacheExpiry">The time at which the new snapshot becomes stale.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.StatisticsRefreshed,
        Level = LogLevel.Information,
        Message = "Statistics refreshed: {TotalRecords} total records, cache expires at {CacheExpiry}" )]
    private static partial void LogStatisticsRefreshed( ILogger logger, int totalRecords, DateTimeOffset cacheExpiry );

    /// <summary>Logs (Warning) that the cache-bootstrap status could not be read from Redis.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception raised while reading Redis.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.CacheBootstrapStatusReadError,
        Level = LogLevel.Warning,
        Message = "Failed to read cache bootstrap status from Redis" )]
    private static partial void LogCacheBootstrapStatusError( ILogger logger, Exception ex );

    /// <summary>Logs (Information) that a refresh was skipped because the cache is still fresh.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.StatisticsRefreshSkipped,
        Level = LogLevel.Information,
        Message = "Statistics refresh skipped — cache is still fresh" )]
    private static partial void LogStatisticsRefreshSkipped( ILogger logger );

    #endregion LoggerMessage Definitions
}
