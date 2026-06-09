namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Statistics about the lookup collection including counts, provider breakdown, and recent entries.
/// </summary>
public sealed class LookupStatistics {

    /// <summary>
    /// Total number of lookup records in the collection.
    /// </summary>
    public int TotalRecords { get; init; }

    /// <summary>
    /// Number of album lookups.
    /// </summary>
    public int AlbumCount { get; init; }

    /// <summary>
    /// Number of track lookups.
    /// </summary>
    public int TrackCount { get; init; }

    /// <summary>
    /// Breakdown of record counts by provider.
    /// </summary>
    public Dictionary<string, int> ProviderCounts { get; init; } = [];

    /// <summary>
    /// The five most recently looked up entries.
    /// </summary>
    public List<RecentLookupEntry> RecentEntries { get; init; } = [];

    /// <summary>
    /// Earliest lookup timestamp in the collection.
    /// </summary>
    public DateTimeOffset? EarliestLookup { get; init; }

    /// <summary>
    /// Latest lookup timestamp in the collection.
    /// </summary>
    public DateTimeOffset? LatestLookup { get; init; }

    /// <summary>
    /// Timestamp when these statistics were generated.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Status of the cache bootstrap background service.
    /// </summary>
    public CacheBootstrapStatus? CacheBootstrapStatus { get; init; }

    /// <summary>
    /// Returns a copy of this instance with <see cref="CacheBootstrapStatus"/> replaced by
    /// <paramref name="status"/>. All other properties are carried forward unchanged.
    /// </summary>
    /// <param name="status">The bootstrap status to overlay. May be null.</param>
    /// <returns>A new <see cref="LookupStatistics"/> with the updated status.</returns>
    public LookupStatistics WithBootstrapStatus( CacheBootstrapStatus? status ) =>
        new( ) {
            TotalRecords = TotalRecords,
            AlbumCount = AlbumCount,
            TrackCount = TrackCount,
            ProviderCounts = ProviderCounts,
            RecentEntries = RecentEntries,
            EarliestLookup = EarliestLookup,
            LatestLookup = LatestLookup,
            GeneratedAt = GeneratedAt,
            CacheBootstrapStatus = status
        };
}
