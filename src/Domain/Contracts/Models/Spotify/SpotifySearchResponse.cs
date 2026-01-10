using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// Response from GET /search endpoint.
    /// Contains paginated results for each requested type.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /search?q={query}&amp;type={types}
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/search
    /// 
    /// Search query modifiers:
    /// - track:{name}    Filter by track name
    /// - artist:{name}   Filter by artist name
    /// - album:{name}    Filter by album name
    /// - year:{yyyy}     Filter by release year
    /// - isrc:{code}     Filter by ISRC (tracks only)
    /// - upc:{code}      Filter by UPC (albums only)
    /// - genre:{genre}   Filter by genre
    /// 
    /// Example queries:
    /// - "isrc:USRC12345678" → Returns track with that ISRC
    /// - "upc:012345678901" → Returns album with that UPC
    /// - "track:Alive artist:Daft Punk" → Returns tracks matching both
    /// </remarks>
    public sealed class SpotifySearchResponse {

        /// <summary>
        /// Search results for tracks. Only present if "track" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "tracks" )]
        public SpotifyPaging<SpotifyTrack>? Tracks { get; set; }

        /// <summary>
        /// Search results for albums. Only present if "album" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "albums" )]
        public SpotifyPaging<SpotifyAlbum>? Albums { get; set; }

        /// <summary>
        /// Search results for artists. Only present if "artist" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public SpotifyPaging<SpotifyArtist>? Artists { get; set; }

        /// <summary>
        /// Search results for playlists. Only present if "playlist" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "playlists" )]
        public SpotifyPaging<SpotifyPlaylistSimplified>? Playlists { get; set; }

        /// <summary>
        /// Search results for shows. Only present if "show" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "shows" )]
        public SpotifyPaging<SpotifyShowSimplified>? Shows { get; set; }

        /// <summary>
        /// Search results for episodes. Only present if "episode" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "episodes" )]
        public SpotifyPaging<SpotifyEpisodeSimplified>? Episodes { get; set; }

        /// <summary>
        /// Search results for audiobooks. Only present if "audiobook" was in the type parameter.
        /// </summary>
        [JsonPropertyName( "audiobooks" )]
        public SpotifyPaging<SpotifyAudiobookSimplified>? Audiobooks { get; set; }
    }

}
