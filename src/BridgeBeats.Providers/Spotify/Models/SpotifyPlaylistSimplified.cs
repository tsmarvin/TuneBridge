using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Spotify.Models {

    /// <summary>
    /// Simplified playlist object for search results.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /search?type=playlist
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/search
    /// </remarks>
    public sealed class SpotifyPlaylistSimplified {

        /// <summary>
        /// The Spotify ID for the playlist.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The name of the playlist.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Known external URLs for this playlist.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// A link to the Web API endpoint providing full details of the playlist.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify URI for the playlist.
        /// </summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
