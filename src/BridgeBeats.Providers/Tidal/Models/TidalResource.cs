using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Tidal.Models {

    /// <summary>
    /// Base resource object for all Tidal API entities following JSON:API specification.
    /// All Tidal resources (tracks, albums, artists) share this base structure.
    /// </summary>
    /// <remarks>
    /// JSON:API resource objects MUST contain at least 'type' and 'id' members.
    /// Documentation: https://jsonapi.org/format/#document-resource-objects
    /// </remarks>
    public sealed class TidalResource {

        /// <summary>
        /// The resource type identifier (e.g., "tracks", "albums", "artists", "artworks").
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The unique identifier for this resource.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The resource's attributes containing the actual data.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public TidalAttributes? Attributes { get; set; }

        /// <summary>
        /// The resource's relationships to other resources.
        /// </summary>
        [JsonPropertyName( "relationships" )]
        public TidalRelationships? Relationships { get; set; }
    }

}
