using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Infrastructure.Cache.Entities;

/// <summary>
/// Represents a lookup value that can be used to find a MediaLinkCacheEntry.
/// This includes user input links, service-provided links, external IDs, and metadata combinations.
/// </summary>
public class MediaLookupEntry {

    /// <summary>
    /// Primary key for the lookup entry.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The lookup value. This could be:
    /// - A normalized URL (user input or service link)
    /// - An external ID (ISRC or UPC)
    /// - A metadata combination (title|artist)
    /// </summary>
    public string LookupValue { get; set; } = string.Empty;

    /// <summary>
    /// The type of lookup entry. Used to differentiate between:
    /// - UserInput: Original user-provided link
    /// - ServiceLink: Link from a music service (Apple Music, Spotify, Tidal)
    /// - ExternalId: ISRC or UPC identifier
    /// - Metadata: Title and artist combination
    /// </summary>
    public LookupEntryType LookupType { get; set; }

    /// <summary>
    /// Whether this lookup is for an album (true) or track (false).
    /// This is crucial for metadata-based lookups where the same title+artist could represent different media types.
    /// </summary>
    public bool IsAlbum { get; set; }

    /// <summary>
    /// Foreign key to the associated cache entry (using Rkey).
    /// </summary>
    public string MediaLinkCacheEntryRkey { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property to the parent cache entry.
    /// </summary>
    public MediaLinkCacheEntry? MediaLinkCacheEntry { get; set; }

    /// <summary>
    /// The timestamp when this lookup entry was first added to the cache.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
