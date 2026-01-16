namespace BridgeBeats.Domain.Types.Enums {
    /// <summary>
    /// The type of lookup request being made.
    /// </summary>
    public enum LookupRequestType {
        /// <summary>
        /// Used when looking up a song/track by its isrc/external ID.
        /// </summary>
        IsrcLookup,

        /// <summary>
        /// Used when looking up an album by its upc/external ID.
        /// </summary>
        UpcLookup,

        /// <summary>
        /// Used when looking up an artist by name.
        /// </summary>
        ArtistLookup,

        /// <summary>
        /// Used when looking up a track or album by its provider URI.
        /// </summary>
        UriLookup,

        /// <summary>
        /// Used when looking up an album by its title and artist.
        /// </summary>
        AlbumLookup,

        /// <summary>
        /// Used when looking up an album from a specific artist.
        /// </summary>
        ArtistAlbumLookup,

        /// <summary>
        /// Used when looking up a track from a specific album.
        /// </summary>
        AlbumTrackLookup,

        /// <summary>
        /// Used when looking up a song/track by its title and artist.
        /// </summary>
        SongLookup,

        /// <summary>
        /// Used when looking up an album directly by its provider ID.
        /// </summary>
        AlbumIdLookup,

        /// <summary>
        /// Used when looking up a song/track directly by its provider ID.
        /// </summary>
        SongIdLookup
    }
}
