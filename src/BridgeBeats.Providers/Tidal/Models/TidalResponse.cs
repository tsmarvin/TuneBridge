using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Tidal.Models {

    /// <summary>
    /// Top-level response wrapper for Tidal API responses following JSON:API specification.
    /// Contains the primary resource data and related resources in the included array.
    /// </summary>
    /// <typeparam name="T">
    /// The type of the primary resource data. For endpoints that return a single resource,
    /// use the resource type (for example, <c>TidalResource</c>). For endpoints that return
    /// multiple resources, use a collection type (for example, <c>List&lt;TidalResource&gt;</c>).
    /// </typeparam>
    /// <remarks>
    /// Tidal API follows JSON:API v1.0 specification with 'data' and 'included' top-level members.
    /// The 'data' member can be either a single resource object or an array of resource objects;
    /// this distinction is represented here by the generic type parameter <typeparamref name="T" />.
    /// Documentation: https://jsonapi.org/format/
    /// </remarks>
    public sealed class TidalResponse<T> {

        /// <summary>
        /// The document's primary data, whose shape depends on <typeparamref name="T" />.
        /// This will be a single resource when <typeparamref name="T" /> is a resource type
        /// (for example, <c>TidalResource</c>), or a collection of resources when
        /// <typeparamref name="T" /> is a collection type (for example, <c>List&lt;TidalResource&gt;</c>).
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
