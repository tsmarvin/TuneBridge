namespace TuneBridge.Domain.Contracts.Entities {

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
