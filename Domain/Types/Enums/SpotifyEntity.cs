namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Categorizes the different types of content entities that can be extracted from Spotify URLs.
    /// Used by URL parsers and API clients to determine which Spotify Web API endpoints to call
    /// and how to process the returned metadata.
    /// </summary>
    /// <remarks>
    /// The enum values use bit flags to allow for future combination scenarios, though current
    /// implementation treats them as discrete types. Track and Album are the primary types used
    /// for cross-platform music matching.
    /// </remarks>
    public enum SpotifyEntity {
        /// <summary>
        /// Unrecognized or unsupported Spotify URL format. Indicates parsing failure or an entity type
        /// that's not currently handled by the application (e.g., podcasts, shows, episodes).
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Album entity representing a collection of tracks. Maps to Spotify's /albums API endpoint.
        /// Albums are matched across platforms using UPC codes when available.
        /// </summary>
        Album = 1,

        /// <summary>
        /// Artist entity representing a music creator or band. Maps to Spotify's /artists API endpoint.
        /// Used during title/artist searches to filter results by artist ID.
        /// </summary>
        Artist = 2,

        /// <summary>
        /// Track entity representing a single song/recording. Maps to Spotify's /tracks API endpoint.
        /// Tracks are matched across platforms using ISRC codes when available. This is the most
        /// commonly shared entity type in chat applications.
        /// </summary>
        Track = 4,

        /// <summary>
        /// Playlist entity representing a user-curated or editorial collection of tracks. Maps to
        /// Spotify's /playlists API endpoint. Currently parsed but not fully supported for cross-platform
        /// matching as playlists are platform-specific.
        /// </summary>
        Playlist = 8
    }
}
