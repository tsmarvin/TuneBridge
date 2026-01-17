using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.AppleMusic.Models {

    /// <summary>
    /// Preview information for Apple Music content.
    /// </summary>
    public sealed class AppleMusicPreview {

        /// <summary>
        /// The preview URL for the content.
        /// </summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
