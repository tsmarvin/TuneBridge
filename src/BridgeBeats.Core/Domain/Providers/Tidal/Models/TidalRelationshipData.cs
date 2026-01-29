using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// Relationship data containing resource identifiers.
    /// </summary>
    /// <remarks>
    /// JSON:API relationship objects contain a 'data' member with resource identifier objects.
    /// Each identifier has a 'type' and 'id' referencing a resource in the 'included' array.
    /// </remarks>
    public sealed class TidalRelationshipData {

        /// <summary>
        /// Array of resource identifiers (type + id pairs).
        /// </summary>
        [JsonPropertyName( "data" )]
        public List<TidalResourceIdentifier>? Data { get; set; }
    }

}
