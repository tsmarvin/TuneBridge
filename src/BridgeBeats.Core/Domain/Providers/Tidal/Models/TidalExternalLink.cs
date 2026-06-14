using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring an entry in a Tidal resource's <c>externalLinks</c> array.
    /// </summary>
    /// <remarks>
    /// Carries the public-facing Tidal URL for a track or album. Used to populate the
    /// resolved result's link. The shape mirrors Tidal's wire format and is a
    /// deserialization target only.
    /// </remarks>
    public sealed class TidalExternalLink {

        /// <summary>
        /// Gets or sets the link target, mapped from the Tidal <c>href</c> member.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;
    }

}
