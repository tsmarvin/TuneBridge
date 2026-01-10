using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Tidal {

    /// <summary>
    /// Resource identifier containing type and id to reference a resource.
    /// </summary>
    /// <remarks>
    /// JSON:API resource identifier objects MUST contain 'type' and 'id'.
    /// They are used in relationships to reference resources in the 'included' array.
    /// </remarks>
    public sealed class TidalResourceIdentifier {

        /// <summary>
        /// The resource type (e.g., "artists", "albums", "tracks").
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The unique identifier for the resource.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;
    }

}
