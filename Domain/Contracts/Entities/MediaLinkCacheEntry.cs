namespace TuneBridge.Domain.Contracts.Entities {

    /// <summary>
    /// Represents a cached MediaLinkResult entry stored in the SQLite database.
    /// This entity tracks the Bluesky PDS record location and associated input links.
    /// The actual MediaLinkResult data is always fetched from the PDS to ensure freshness.
    /// </summary>
    public class MediaLinkCacheEntry {

        /// <summary>
        /// Primary key for the cache entry.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The AT-URI of the record on Bluesky PDS (e.g., at://did:plc:xxx/media.tunebridge.lookup.result/yyy).
        /// </summary>
        public string RecordUri { get; set; } = string.Empty;

        /// <summary>
        /// The timestamp when this cache entry was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The timestamp when this record was last looked up (either created or refreshed on PDS).
        /// Used to determine if the record needs to be updated.
        /// </summary>
        public DateTime LastLookedUpAt { get; set; }

        /// <summary>
        /// Navigation property for related input links.
        /// </summary>
        public List<InputLinkEntry> InputLinks { get; set; } = [];
    }

    /// <summary>
    /// Represents an input link that generated (or could generate) a MediaLinkResult.
    /// Multiple input links can map to the same cache entry.
    /// </summary>
    public class InputLinkEntry {

        /// <summary>
        /// Primary key for the input link entry.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The normalized input link (URL).
        /// </summary>
        public string Link { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key to the associated cache entry.
        /// </summary>
        public int MediaLinkCacheEntryId { get; set; }

        /// <summary>
        /// Navigation property to the parent cache entry.
        /// </summary>
        public MediaLinkCacheEntry? MediaLinkCacheEntry { get; set; }

        /// <summary>
        /// The timestamp when this link was first added to the cache.
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}
