using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring an entry in a Tidal artwork resource's <c>files</c> array.
    /// </summary>
    /// <remarks>
    /// Carries the URL of a single album artwork image file. The first entry is used as
    /// the album art URL for the resolved result. The shape mirrors Tidal's wire format
    /// and is a deserialization target only.
    /// </remarks>
    public sealed class TidalFile {

        /// <summary>
        /// Gets or sets the image file location, mapped from the Tidal <c>href</c>
        /// member.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;
    }

}
