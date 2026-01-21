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
}

/// <summary>
/// Summary of a recent lookup entry for display.
/// </summary>
public sealed class RecentLookupEntry {

    /// <summary>
    /// The AT-URI of the record.
    /// </summary>
    public string AtUri { get; init; } = string.Empty;

    /// <summary>
    /// Whether this is an album (true) or track (false).
    /// </summary>
    public bool IsAlbum { get; init; }

    /// <summary>
    /// The artist name.
    /// </summary>
    public string Artist { get; init; } = string.Empty;

    /// <summary>
    /// The title of the track or album.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// When the lookup was performed.
    /// </summary>
    public DateTimeOffset? LookedUpAt { get; init; }

    /// <summary>
    /// The card ID for linking to the details page.
    /// </summary>
    public string? CardId { get; init; }
}
