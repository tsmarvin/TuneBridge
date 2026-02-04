using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

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

}
