namespace BridgeBeats.Contracts.DTOs {

    /// <summary>
    /// One row of the statistics "recent lookups" feed, describing a single stored lookup for
    /// display.
    /// </summary>
    public sealed class RecentLookupEntry {

        /// <summary>
        /// The AT-URI (<c>at://…</c>) of the stored lookup record. Defaults to an empty string
        /// until populated.
        /// </summary>
        public string AtUri { get; init; } = string.Empty;

        /// <summary>
        /// <see langword="true"/> when the lookup resolved to an album, <see langword="false"/>
        /// when it resolved to a track.
        /// </summary>
        public bool IsAlbum { get; init; }

        /// <summary>
        /// The artist name of the looked-up item. Defaults to an empty string until populated.
        /// </summary>
        public string Artist { get; init; } = string.Empty;

        /// <summary>
        /// The title of the looked-up item. Defaults to an empty string until populated.
        /// </summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// When the lookup occurred, or <see langword="null"/> if not recorded.
        /// </summary>
        public DateTimeOffset? LookedUpAt { get; init; }

        /// <summary>
        /// The card identifier used to link to the details page, or <see langword="null"/> when none.
        /// </summary>
        public string? CardId { get; init; }
    }

}
