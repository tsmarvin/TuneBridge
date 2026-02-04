using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// File object containing a URL to a file resource (e.g., album artwork).
    /// </summary>
    /// <remarks>
    /// Used in the 'files' array of artwork attributes.
    /// Provides URLs to different sizes of album artwork images.
    /// </remarks>
    public sealed class TidalFile {

        /// <summary>
        /// The URL to the file resource.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;
    }

}
