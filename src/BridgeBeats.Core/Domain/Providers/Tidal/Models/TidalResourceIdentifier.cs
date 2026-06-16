using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring a Tidal API (JSON:API-style) resource identifier object.
    /// </summary>
    /// <remarks>
    /// A resource identifier is the lightweight <c>type</c>/<c>id</c> reference that a
    /// relationship uses to point at a resource without embedding it. The referenced
    /// resource's full attributes are resolved from the response's
    /// <see cref="TidalResponse{T}.Included"/> array by matching this identifier.
    /// </remarks>
    public sealed class TidalResourceIdentifier {

        /// <summary>
        /// Gets or sets the referenced resource's type, mapped from the Tidal
        /// <c>type</c> member (for example <c>"artists"</c> or <c>"genres"</c>).
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the referenced resource's identifier, mapped from the Tidal
        /// <c>id</c> member.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;
    }

}
