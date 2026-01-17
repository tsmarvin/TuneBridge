using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Spotify.Models {

    /// <summary>
    /// A simplified artist object returned in track/album responses and search results.
    /// Contains basic identification but not full artist details.
    /// </summary>
    /// <remarks>
    /// Endpoint: Embedded in track/album objects
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-an-artist
    /// </remarks>
    public sealed class SpotifyArtistSimplified {

        /// <summary>
        /// Known external URLs for this artist.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// A link to the Web API endpoint providing full details of the artist.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify ID for the artist.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The name of the artist.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The object type. Always "artist".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "artist";

        /// <summary>
        /// The Spotify URI for the artist.
        /// </summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
