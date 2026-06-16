using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring the top-level Tidal API (JSON:API-style) response document.
    /// </summary>
    /// <remarks>
    /// A Tidal response carries a primary <c>data</c> member (a single resource or a
    /// collection, depending on the endpoint) plus a flat <c>included</c> array of
    /// side-loaded related resources (artists, albums, artworks, genres). Relationships
    /// on the primary resource reference these included resources by type and id rather
    /// than embedding them. The shape mirrors Tidal's wire format and is a
    /// deserialization target only.
    /// </remarks>
    /// <typeparam name="T">
    /// The type of the primary <c>data</c> member. In this codebase this is typically
    /// <see cref="TidalResource"/> for single-resource lookups.
    /// </typeparam>
    public sealed class TidalResponse<T> {

        /// <summary>
        /// Gets or sets the primary resource (or collection) of the response, mapped
        /// from the Tidal <c>data</c> member. May be <see langword="null"/> when the
        /// response carries no primary data.
        /// </summary>
        [JsonPropertyName( "data" )]
        public T? Data { get; set; }

        /// <summary>
        /// Gets or sets the side-loaded related resources, mapped from the Tidal
        /// <c>included</c> array. May be <see langword="null"/> when no related
        /// resources are returned.
        /// </summary>
        [JsonPropertyName( "included" )]
        public List<TidalResource>? Included { get; set; }
    }

}
