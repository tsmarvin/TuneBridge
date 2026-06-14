using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring the linkage member of a Tidal API (JSON:API-style) relationship.
    /// </summary>
    /// <remarks>
    /// A relationship's <c>data</c> member holds the list of resource identifiers the
    /// relationship points at (for example the artists or genres linked to a track or
    /// album). Each identifier is resolved against the response's
    /// <see cref="TidalResponse{T}.Included"/> array to obtain the related resource's
    /// attributes.
    /// </remarks>
    public sealed class TidalRelationshipData {

        /// <summary>
        /// Gets or sets the linked resource identifiers, mapped from the relationship's
        /// Tidal <c>data</c> member. May be <see langword="null"/> when the
        /// relationship is empty.
        /// </summary>
        [JsonPropertyName( "data" )]
        public List<TidalResourceIdentifier>? Data { get; set; }
    }

}
