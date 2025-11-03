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
}
