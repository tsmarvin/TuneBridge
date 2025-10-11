namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Categorizes the different types of content entities that can be extracted from SoundCloud URLs.
    /// Used by URL parsers and API clients to determine which SoundCloud API endpoints to call
    /// and how to process the returned metadata.
    /// </summary>
    public enum SoundCloudEntity {
        /// <summary>
        /// Unrecognized or unsupported SoundCloud URL format. Indicates parsing failure or an entity type
        /// that's not currently handled by the application (e.g., users, comments).
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Track entity representing a single audio track. Maps to SoundCloud's /tracks API endpoint.
        /// This is the most commonly shared entity type in chat applications.
        /// </summary>
        Track = 1,

        /// <summary>
        /// Playlist entity representing a user-curated collection of tracks. Maps to
        /// SoundCloud's /playlists API endpoint. Currently parsed but not fully supported for cross-platform
        /// matching as playlists are platform-specific.
        /// </summary>
        Playlist = 2
    }
}
