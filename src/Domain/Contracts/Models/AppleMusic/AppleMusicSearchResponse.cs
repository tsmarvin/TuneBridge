using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.AppleMusic {

    /// <summary>
    /// Response from the Apple Music search endpoint.
    /// Contains search results organized by resource type.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/search?term={query}&amp;types={types}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/search_for_catalog_resources
    /// 
    /// Example query: GET /v1/catalog/us/search?types=artists&amp;term=Queen
    /// </remarks>
    public sealed class AppleMusicSearchResponse {

        /// <summary>
        /// The search results organized by resource type.
        /// </summary>
        [JsonPropertyName( "results" )]
        public AppleMusicSearchResults? Results { get; set; }

    }

    /// <summary>
    /// Contains the search results for each requested resource type.
    /// Each property represents a different resource type (artists, albums, songs, etc.).
    /// </summary>
    public sealed class AppleMusicSearchResults {

        /// <summary>
        /// Search results for artists.
        /// Only present if "artists" was in the types parameter.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public AppleMusicDataResponse<AppleMusicArtist>? Artists { get; set; }

        /// <summary>
        /// Search results for albums.
        /// Only present if "albums" was in the types parameter.
        /// </summary>
        [JsonPropertyName( "albums" )]
        public AppleMusicDataResponse<AppleMusicAlbum>? Albums { get; set; }

        /// <summary>
        /// Search results for songs.
        /// Only present if "songs" was in the types parameter.
        /// </summary>
        [JsonPropertyName( "songs" )]
        public AppleMusicDataResponse<AppleMusicSong>? Songs { get; set; }

    }

}
