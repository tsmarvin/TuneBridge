using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the top-level Apple Music API search response, with results organized by resource type.
    /// Deserialization target only.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/search?term={query}&amp;types={types}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/search_for_catalog_resources
    ///
    /// Example query: GET /v1/catalog/us/search?types=artists&amp;term=Queen
    /// </remarks>
    public sealed class AppleMusicSearchResponse {

        /// <summary>The grouped search results; <see langword="null"/> when the response contains none.</summary>
        [JsonPropertyName( "results" )]
        public AppleMusicSearchResults? Results { get; set; }

    }

}
