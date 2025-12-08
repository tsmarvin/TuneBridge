namespace BridgeBeats.Web.Models {

    /// <summary>
    /// View model for the user's playlists management page.
    /// </summary>
    public class PlaylistsViewModel {

        /// <summary>
        /// List of playlists owned by the user.
        /// </summary>
        public List<PlaylistSummary> Playlists { get; set; } = [];

        /// <summary>
        /// Optional error message to display.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Summary information for a single playlist.
        /// </summary>
        public class PlaylistSummary {
            /// <summary>
            /// The unique identifier for the playlist.
            /// </summary>
            public string PlaylistId { get; set; } = string.Empty;

            /// <summary>
            /// Title of the playlist.
            /// </summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>
            /// Optional description of the playlist.
            /// </summary>
            public string? Description { get; set; }

            /// <summary>
            /// Number of items in the playlist.
            /// </summary>
            public int ItemCount { get; set; }

            /// <summary>
            /// When the playlist was created.
            /// </summary>
            public DateTime CreatedAt { get; set; }

            /// <summary>
            /// Full URL to view the playlist.
            /// </summary>
            public string PlaylistUrl { get; set; } = string.Empty;
        }
    }
}
