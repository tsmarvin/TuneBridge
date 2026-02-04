namespace BridgeBeats.Contracts.DTOs {

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

}
