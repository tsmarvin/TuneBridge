namespace TuneBridge.Domain.Types.Enums {
    /// <summary>
    /// Represents the different types of entities that can be parsed from Spotify URLs.
    /// </summary>
    public enum SpotifyEntity {
        /// <summary>
        /// Unknown or unsupported entity type.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// An album entity.
        /// </summary>
        Album = 1,

        /// <summary>
        /// An artist entity.
        /// </summary>
        Artist = 2,

        /// <summary>
        /// A track (song) entity.
        /// </summary>
        Track = 4,

        /// <summary>
        /// A playlist entity.
        /// </summary>
        Playlist = 8
    }
}
