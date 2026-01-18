using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Tidal.Models {

    /// <summary>
    /// External link object containing a URL to an external resource.
    /// </summary>
    /// <remarks>
    /// Used in the 'externalLinks' array of track and album attributes.
    /// Provides URLs to play the resource on Tidal's web player.
    /// </remarks>
    public sealed class TidalExternalLink {

        /// <summary>
        /// The URL to the external resource.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;
    }

}
