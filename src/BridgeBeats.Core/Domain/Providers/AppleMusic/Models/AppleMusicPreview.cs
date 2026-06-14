using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring an Apple Music API song <c>preview</c> object. Deserialization target only.
    /// </summary>
    public sealed class AppleMusicPreview {

        /// <summary>The URL of the audio preview clip.</summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
