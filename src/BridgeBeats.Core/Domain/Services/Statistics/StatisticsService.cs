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
/// Service for retrieving and caching statistics about the lookup collection.
/// Maintains an in-memory cache that is refreshed by a background service.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="StatisticsService"/> class.
/// </remarks>
/// <param name="atProtoStorage">Service for accessing ATProto storage.</param>
/// <param name="redis">Redis connection multiplexer for reading cache bootstrap status.</param>
/// <param name="settings">Configuration settings.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed partial class StatisticsService(
    IATProtoStorageService atProtoStorage,
    IConnectionMultiplexer redis,
    StatisticsSettings settings,
    ILogger<StatisticsService> logger
) : IStatisticsService {

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _cacheLock = new( 1, 1 );
    private LookupStatistics? _cachedStats;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private volatile int _isRefreshing;

    private readonly Channel<bool> _refreshChannel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropNewest }
    );

    /// <summary>
    /// Gets the <see cref="ChannelReader{T}"/> that the background service reads from
    /// to receive manual refresh signals.
    /// </summary>
    public ChannelReader<bool> RefreshTriggerReader => _refreshChannel.Reader;

    /// <inheritdoc/>
    public bool IsRefreshing => Interlocked.CompareExchange( ref _isRefreshing, 0, 0 ) == 1;

    /// <inheritdoc/>
    public LookupStatistics? GetCachedStatistics( ) {
        return _cachedStats;
    }

    /// <inheritdoc/>
    public bool TriggerRefresh( ) {
        return !IsRefreshing && _refreshChannel.Writer.TryWrite( true );
    }

    /// <inheritdoc/>
    public Task<LookupStatistics> GetStatisticsAsync( CancellationToken cancellationToken = default ) {
        LookupStatistics stats = _cachedStats ?? new LookupStatistics { GeneratedAt = DateTimeOffset.MinValue };
        return Task.FromResult( stats );
    }

    /// <inheritdoc/>
    public async Task<LookupStatistics> RefreshStatisticsAsync( CancellationToken cancellationToken = default ) {
        return await RefreshStatisticsAsync( false, cancellationToken );
    }

    /// <inheritdoc/>
    public async Task<CacheBootstrapStatus?> GetLiveBootstrapStatusAsync( CancellationToken cancellationToken = default ) =>
        await GetCacheBootstrapStatusAsync( ).WaitAsync( cancellationToken );

    /// <summary>
    /// Refreshes cached statistics, optionally forcing recomputation even when cache is fresh.
    /// </summary>
    /// <param name="forceRefresh">True to bypass freshness check and recompute immediately.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The refreshed lookup statistics.</returns>
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

                LookupStatistics stats = await ComputeStatisticsAsync(cancellationToken);

                _cachedStats = stats;
                _cacheExpiry = DateTimeOffset.UtcNow + settings.CacheDuration;

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
    /// Computes statistics by fetching all records from ATProto PDS.
    /// </summary>
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
    /// Retrieves the cache bootstrap status from Redis.
    /// </summary>
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
    /// Creates a RecentLookupEntry from an AT-URI and MediaLinkResult.
    /// </summary>
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

    /// <summary>
    /// Logs that statistics are being refreshed from ATProto PDS.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.RefreshingStatistics,
        Level = LogLevel.Information,
        Message = "Refreshing statistics from ATProto PDS: {PdsUri}, DID: {UserDid}" )]
    private static partial void LogRefreshingStatistics( ILogger logger, string pdsUri, string userDid );

    /// <summary>
    /// Logs that statistics have been refreshed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.StatisticsRefreshed,
        Level = LogLevel.Information,
        Message = "Statistics refreshed: {TotalRecords} total records, cache expires at {CacheExpiry}" )]
    private static partial void LogStatisticsRefreshed( ILogger logger, int totalRecords, DateTimeOffset cacheExpiry );

    /// <summary>
    /// Logs failure to read cache bootstrap status from Redis.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.CacheBootstrapStatusReadError,
        Level = LogLevel.Warning,
        Message = "Failed to read cache bootstrap status from Redis" )]
    private static partial void LogCacheBootstrapStatusError( ILogger logger, Exception ex );

    /// <summary>
    /// Logs that a statistics refresh was skipped because the cache is still fresh.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Services.Other.StatisticsRefreshSkipped,
        Level = LogLevel.Information,
        Message = "Statistics refresh skipped — cache is still fresh" )]
    private static partial void LogStatisticsRefreshSkipped( ILogger logger );

    #endregion LoggerMessage Definitions
}
