using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring a Tidal API (JSON:API-style) resource object.
    /// </summary>
    /// <remarks>
    /// A resource object is the unit of data in a Tidal response. It is identified by
    /// the <see cref="Type"/>/<see cref="Id"/> pair (for example <c>"tracks"</c>,
    /// <c>"albums"</c>, <c>"artists"</c>, <c>"artworks"</c>, <c>"genres"</c>), carries
    /// its own field values in <see cref="Attributes"/>, and links to other resources
    /// via <see cref="Relationships"/>. Resources appear both as the primary
    /// <see cref="TidalResponse{T}.Data"/> and as entries in
    /// <see cref="TidalResponse{T}.Included"/>.
    /// </remarks>
    public sealed class TidalResource {

        /// <summary>
        /// Gets or sets the resource type, mapped from the Tidal <c>type</c> member
        /// (for example <c>"tracks"</c>, <c>"albums"</c>, <c>"artists"</c>).
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource identifier, mapped from the Tidal <c>id</c> member.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource's field values, mapped from the Tidal
        /// <c>attributes</c> object. May be <see langword="null"/> when the resource
        /// is referenced only as an identifier.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public TidalAttributes? Attributes { get; set; }

        /// <summary>
        /// Gets or sets the resource's links to related resources, mapped from the
        /// Tidal <c>relationships</c> object. May be <see langword="null"/> when no
        /// relationships are returned.
        /// </summary>
        [JsonPropertyName( "relationships" )]
        public TidalRelationships? Relationships { get; set; }
    }

}
