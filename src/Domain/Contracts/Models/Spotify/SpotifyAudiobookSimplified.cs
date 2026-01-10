using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// Simplified audiobook object for search results.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /search?type=audiobook
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/search
    /// </remarks>
    public sealed class SpotifyAudiobookSimplified {

        /// <summary>
        /// The Spotify ID for the audiobook.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The name of the audiobook.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Known external URLs for this audiobook.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// A link to the Web API endpoint providing full details of the audiobook.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify URI for the audiobook.
        /// </summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
