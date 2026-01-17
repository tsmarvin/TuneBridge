using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Spotify.Models {

    /// <summary>
    /// Linked track information when track relinking is applied.
    /// Contains information about the originally requested track when it has been
    /// replaced with a different version due to market availability.
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyLinkedTrack {

        /// <summary>
        /// Known external URLs for this track.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// A link to the Web API endpoint providing full details of the track.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify ID for the track.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The object type. Always "track".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "track";

        /// <summary>
        /// The Spotify URI for the track.
        /// </summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
