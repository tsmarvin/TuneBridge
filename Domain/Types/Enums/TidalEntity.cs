namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Categorizes the different types of content entities that can be extracted from Tidal URLs.
    /// Used by URL parsers and API clients to determine which Tidal API endpoints to call
    /// and how to process the returned metadata.
    /// </summary>
    public enum TidalEntity {
        /// <summary>
        /// Unrecognized or unsupported Tidal URL format. Indicates parsing failure or an entity type
        /// that's not currently handled by the application (e.g., playlists, mixes).
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Album entity representing a collection of tracks. Maps to Tidal's /albums API endpoint.
        /// Albums are matched across platforms using UPC codes when available.
        /// </summary>
        Album = 1,

        /// <summary>
        /// Artist entity representing a music creator or band. Maps to Tidal's /artists API endpoint.
        /// Used during title/artist searches to filter results by artist ID.
        /// </summary>
        Artist = 2,

        /// <summary>
        /// Track entity representing a single song/recording. Maps to Tidal's /tracks API endpoint.
        /// Tracks are matched across platforms using ISRC codes when available. This is the most
        /// commonly shared entity type in chat applications.
        /// </summary>
        Track = 4,
    }
}
