using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Tidal {

    /// <summary>
    /// Top-level response wrapper for Tidal API responses following JSON:API specification.
    /// Contains the primary resource data and related resources in the included array.
    /// </summary>
    /// <typeparam name="T">The type of the primary resource data.</typeparam>
    /// <remarks>
    /// Tidal API follows JSON:API v1.0 specification with 'data' and 'included' top-level members.
    /// Documentation: https://jsonapi.org/format/
    /// </remarks>
    public sealed class TidalResponse<T> {

        /// <summary>
        /// The document's primary data. Can be a single resource or an array of resources.
        /// </summary>
        [JsonPropertyName( "data" )]
        public T? Data { get; set; }

        /// <summary>
        /// Related resources that are included in the response.
        /// Used to avoid additional requests for related entities (artists, albums, artworks).
        /// </summary>
        [JsonPropertyName( "included" )]
        public List<TidalResource>? Included { get; set; }
    }

}
