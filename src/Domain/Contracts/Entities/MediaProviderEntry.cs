using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Contracts.Entities {

    /// <summary>
    /// Represents a provider-specific identifier for a cached media item.
    /// Each provider (Apple Music, Spotify, Tidal) may have its own unique ID for the same track or album.
    /// </summary>
    public class MediaProviderEntry {

        /// <summary>
        /// Primary key for the provider entry.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The music provider that this ID belongs to.
        /// </summary>
        public SupportedProviders Provider { get; set; }

        /// <summary>
        /// The provider-specific identifier for the track or album.
        /// For Apple Music: the catalog ID (e.g., "1234567890")
        /// For Spotify: the track/album ID (e.g., "3n3Ppam7vgaVa1iaRUc9Lp")
        /// For Tidal: the track/album ID
        /// </summary>
        public string ProviderId { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key to the associated cache entry (using Rkey).
        /// </summary>
        public string MediaLinkCacheEntryRkey { get; set; } = string.Empty;

        /// <summary>
        /// Navigation property to the parent cache entry.
        /// </summary>
        public MediaLinkCacheEntry? MediaLinkCacheEntry { get; set; }

        /// <summary>
        /// The timestamp when this provider entry was first added to the cache.
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}
