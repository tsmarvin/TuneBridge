using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Tidal.Models {

    /// <summary>
    /// Relationships object containing references to related Tidal resources.
    /// </summary>
    /// <remarks>
    /// JSON:API relationships contain 'data' with resource identifiers (type + id).
    /// The full resource objects are in the top-level 'included' array.
    /// </remarks>
    public sealed class TidalRelationships {

        /// <summary>
        /// Relationship to artist resources.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public TidalRelationshipData? Artists { get; set; }

        /// <summary>
        /// Relationship to album resources (for tracks).
        /// </summary>
        [JsonPropertyName( "albums" )]
        public TidalRelationshipData? Albums { get; set; }

        /// <summary>
        /// Relationship to genre resources.
        /// </summary>
        [JsonPropertyName( "genres" )]
        public TidalRelationshipData? Genres { get; set; }
    }

}
