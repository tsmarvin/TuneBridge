using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.AppleMusic {

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
