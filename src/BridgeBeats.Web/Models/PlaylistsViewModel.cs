namespace BridgeBeats.Web.Models {
    /// <summary>
    /// View model for the playlists listing page, holding the user's playlist summaries and an optional error message.
    /// </summary>
    public class PlaylistsViewModel {
        /// <summary>
        /// The summaries of the playlists to display.
        /// </summary>
        public List<PlaylistSummary> Playlists { get; set; } = [];

        /// <summary>
        /// An optional error message shown when the playlists could not be loaded.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// A lightweight summary of a single playlist for the listing view.
        /// </summary>
        public class PlaylistSummary {
            /// <summary>
            /// The playlist identifier.
            /// </summary>
            public string PlaylistId { get; set; } = string.Empty;

            /// <summary>
            /// The playlist title.
            /// </summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>
            /// An optional playlist description.
            /// </summary>
            public string? Description { get; set; }

            /// <summary>
            /// The number of items in the playlist.
            /// </summary>
            public int ItemCount { get; set; }

            /// <summary>
            /// The timestamp when the playlist was created.
            /// </summary>
            public DateTime CreatedAt { get; set; }

            /// <summary>
            /// The shareable URL of the playlist.
            /// </summary>
            public string PlaylistUrl { get; set; } = string.Empty;
        }
    }
}
