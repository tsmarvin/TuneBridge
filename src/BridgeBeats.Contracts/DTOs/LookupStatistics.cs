namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Aggregate statistics projection over stored lookups, assembled for the stats feed. Combines
/// totals, per-provider counts, a recent-lookups list, the time span covered, and optionally the
/// live cache-bootstrap status.
/// </summary>
public sealed class LookupStatistics {

    /// <summary>
    /// Total number of lookup records counted.
    /// </summary>
    public int TotalRecords { get; init; }

    /// <summary>
    /// Number of records that resolved to an album.
    /// </summary>
    public int AlbumCount { get; init; }

    /// <summary>
    /// Number of records that resolved to a track.
    /// </summary>
    public int TrackCount { get; init; }

    /// <summary>
    /// Record counts keyed by provider name (the provider's string name, not the enum).
    /// </summary>
    public Dictionary<string, int> ProviderCounts { get; init; } = [];

    /// <summary>
    /// The five most recent lookups, ordered newest first for display in the "recent lookups" feed.
    /// </summary>
    public List<RecentLookupEntry> RecentEntries { get; init; } = [];

    /// <summary>
    /// Timestamp of the earliest lookup in the counted set, or <see langword="null"/> if there are
    /// no records.
    /// </summary>
    public DateTimeOffset? EarliestLookup { get; init; }

    /// <summary>
    /// Timestamp of the latest lookup in the counted set, or <see langword="null"/> if there are
    /// no records.
    /// </summary>
    public DateTimeOffset? LatestLookup { get; init; }

    /// <summary>
    /// When this statistics snapshot was generated.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Live status of the cache-bootstrap worker merged into the snapshot, or
    /// <see langword="null"/> when no bootstrap status is attached.
    /// </summary>
    public CacheBootstrapStatus? CacheBootstrapStatus { get; init; }

    /// <summary>
    /// Returns a copy of this snapshot with the supplied cache-bootstrap status attached, leaving
    /// every other value unchanged. Used to merge live worker status into otherwise cached stats.
    /// </summary>
    /// <param name="status">
    /// The bootstrap status to attach, or <see langword="null"/> to clear it.
    /// </param>
    /// <returns>A new <see cref="LookupStatistics"/> identical to this one except for
    /// <see cref="CacheBootstrapStatus"/>.</returns>
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
