using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Spotify.Models {

    /// <summary>
    /// A full artist object with complete details.
    /// Returned by GET /artists/{id} and search endpoints.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /artists/{id}, GET /search?type=artist
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-an-artist
    /// </remarks>
    public sealed class SpotifyArtist {

        /// <summary>
        /// Known external URLs for this artist.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// Information about the followers of the artist.
        /// </summary>
        [JsonPropertyName( "followers" )]
        public SpotifyFollowers? Followers { get; set; }

        /// <summary>
        /// A list of the genres the artist is associated with.
        /// If not yet classified, the array is empty.
        /// </summary>
        [JsonPropertyName( "genres" )]
        public List<string> Genres { get; set; } = [];

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
        /// Images of the artist in various sizes, widest first.
        /// </summary>
        [JsonPropertyName( "images" )]
        public List<SpotifyImage> Images { get; set; } = [];

        /// <summary>
        /// The name of the artist.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The popularity of the artist. Value between 0 and 100, with 100 being the most popular.
        /// Calculated from the popularity of all the artist's tracks.
        /// </summary>
        [JsonPropertyName( "popularity" )]
        public int Popularity { get; set; }

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
