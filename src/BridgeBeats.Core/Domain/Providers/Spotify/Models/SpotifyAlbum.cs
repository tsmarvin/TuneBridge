using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// A full album object with complete details including external IDs (UPC).
    /// Returned by GET /albums/{id} and GET /albums?ids=.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /albums/{id}, GET /albums?ids={ids}, GET /search?type=album
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-an-album
    /// </remarks>
    public sealed class SpotifyAlbum {

        /// <summary>
        /// The type of album: "album", "single", or "compilation".
        /// </summary>
        [JsonPropertyName( "album_type" )]
        public string AlbumType { get; set; } = string.Empty;

        /// <summary>
        /// The number of tracks in the album.
        /// </summary>
        [JsonPropertyName( "total_tracks" )]
        public int TotalTracks { get; set; }

        /// <summary>
        /// The markets in which the album is available (ISO 3166-1 alpha-2 country codes).
        /// NOTE: Only present when market is not supplied in the request.
        /// </summary>
        [JsonPropertyName( "available_markets" )]
        public List<string>? AvailableMarkets { get; set; }

        /// <summary>
        /// Known external URLs for this album.
        /// </summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>
        /// A link to the Web API endpoint providing full details of the album.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The Spotify ID for the album.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The cover art for the album in various sizes, widest first.
        /// </summary>
        [JsonPropertyName( "images" )]
        public List<SpotifyImage> Images { get; set; } = [];

        /// <summary>
        /// The name of the album. If album contains censored words, the title may be truncated.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The date the album was first released.
        /// Depending on precision, format may be "YYYY", "YYYY-MM", or "YYYY-MM-DD".
        /// </summary>
        [JsonPropertyName( "release_date" )]
        public string ReleaseDate { get; set; } = string.Empty;

        /// <summary>
        /// The precision with which release_date value is known: "year", "month", or "day".
        /// </summary>
        [JsonPropertyName( "release_date_precision" )]
        public string ReleaseDatePrecision { get; set; } = string.Empty;

        /// <summary>
        /// Included if a content restriction is applied.
        /// </summary>
        [JsonPropertyName( "restrictions" )]
        public SpotifyRestrictions? Restrictions { get; set; }

        /// <summary>
        /// The object type. Always "album".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "album";

        /// <summary>
        /// The Spotify URI for the album.
        /// </summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// The artists of the album.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public List<SpotifyArtistSimplified> Artists { get; set; } = [];

        /// <summary>
        /// The tracks of the album as a paging object of simplified track objects.
        /// </summary>
        [JsonPropertyName( "tracks" )]
        public SpotifyPaging<SpotifyTrackSimplified>? Tracks { get; set; }

        /// <summary>
        /// The copyright statements of the album.
        /// </summary>
        [JsonPropertyName( "copyrights" )]
        public List<SpotifyCopyright> Copyrights { get; set; } = [];

        /// <summary>
        /// Known external IDs for the album. (e.g. UPC).
        /// </summary>
        [JsonPropertyName( "external_ids" )]
        public SpotifyExternalIds? ExternalIds { get; set; }

        /// <summary>
        /// A list of the genres the album is associated with.
        /// If not yet classified, the array is empty.
        /// </summary>
        [JsonPropertyName( "genres" )]
        public List<string> Genres { get; set; } = [];

        /// <summary>
        /// The label associated with the album.
        /// </summary>
        [JsonPropertyName( "label" )]
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// The popularity of the album. Value between 0 and 100, with 100 being the most popular.
        /// </summary>
        [JsonPropertyName( "popularity" )]
        public int Popularity { get; set; }
    }

}
