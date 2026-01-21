using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Services.Statistics;

/// <summary>
/// Configuration settings for the statistics service.
/// </summary>
/// <param name="PdsUri">The PDS URI to query for records.</param>
/// <param name="UserDid">The DID of the account whose collection to query.</param>
/// <param name="CacheDuration">How long to cache statistics before refreshing.</param>
public sealed record StatisticsSettings(
    Uri PdsUri,
    string UserDid,
    TimeSpan CacheDuration
);

/// <summary>
/// Service for retrieving and caching statistics about the lookup collection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="StatisticsService"/> class.
/// </remarks>
/// <param name="atProtoStorage">Service for accessing ATProto storage.</param>
/// <param name="settings">Configuration settings.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed class StatisticsService(
    IATProtoStorageService atProtoStorage,
    StatisticsSettings settings,
    ILogger<StatisticsService> logger
) : IStatisticsService {

    private readonly SemaphoreSlim _cacheLock = new( 1, 1 );
    private LookupStatistics? _cachedStats;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public async Task<LookupStatistics> GetStatisticsAsync( CancellationToken cancellationToken = default ) {
        // Check if we have valid cached stats
        return _cachedStats is not null && DateTimeOffset.UtcNow < _cacheExpiry
            ? _cachedStats
            : await RefreshStatisticsAsync( cancellationToken );
    }

    /// <inheritdoc/>
    public async Task<LookupStatistics> RefreshStatisticsAsync( CancellationToken cancellationToken = default ) {
        await _cacheLock.WaitAsync( cancellationToken );
        try {
            // Force refresh - do not check cache validity (this is an explicit refresh request)
            logger.LogInformation(
                "Refreshing statistics from ATProto PDS: {PdsUri}, DID: {UserDid}",
                settings.PdsUri,
                settings.UserDid
            );

            LookupStatistics stats = await ComputeStatisticsAsync( cancellationToken );

            _cachedStats = stats;
            _cacheExpiry = DateTimeOffset.UtcNow + settings.CacheDuration;

            logger.LogInformation(
                "Statistics refreshed: {TotalRecords} total records, cache expires at {CacheExpiry}",
                stats.TotalRecords,
                _cacheExpiry
            );

            return stats;
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
                recentCandidates = recentCandidates
                    .OrderByDescending( x => x.LookedUpAt )
                    .Take( 10 )
                    .ToList( );
            }
        }

        // Build recent entries list
        List<RecentLookupEntry> recentEntries = recentCandidates
            .OrderByDescending( x => x.LookedUpAt )
            .Take( 5 )
            .Select( x => CreateRecentEntry( x.AtUri, x.Result ) )
            .ToList( );

        return new LookupStatistics {
            TotalRecords = totalCount,
            AlbumCount = albumCount,
            TrackCount = trackCount,
            ProviderCounts = providerCounts.OrderByDescending( x => x.Value ).ToDictionary( x => x.Key, x => x.Value ),
            RecentEntries = recentEntries,
            EarliestLookup = earliestLookup,
            LatestLookup = latestLookup,
            GeneratedAt = DateTimeOffset.UtcNow
        };
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
}
