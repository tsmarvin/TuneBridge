using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring the Tidal API (JSON:API-style) <c>relationships</c> object on a
    /// resource.
    /// </summary>
    /// <remarks>
    /// Holds the named relationships a track or album exposes. Each relationship links
    /// to other resources by identifier rather than embedding them; the targets are
    /// resolved from the response's <see cref="TidalResponse{T}.Included"/> array.
    /// Only the relationships this lookup consumes are modelled here.
    /// </remarks>
    public sealed class TidalRelationships {

        /// <summary>
        /// Gets or sets the artists relationship, mapped from the Tidal <c>artists</c>
        /// member. May be <see langword="null"/> when not present on the resource.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public TidalRelationshipData? Artists { get; set; }

        /// <summary>
        /// Gets or sets the albums relationship (carried by tracks), mapped from the
        /// Tidal <c>albums</c> member. May be <see langword="null"/> when not present on
        /// the resource.
        /// </summary>
        [JsonPropertyName( "albums" )]
        public TidalRelationshipData? Albums { get; set; }

        /// <summary>
        /// Gets or sets the genres relationship, mapped from the Tidal <c>genres</c>
        /// member. May be <see langword="null"/> when not present on the resource.
        /// </summary>
        [JsonPropertyName( "genres" )]
        public TidalRelationshipData? Genres { get; set; }
    }

}
