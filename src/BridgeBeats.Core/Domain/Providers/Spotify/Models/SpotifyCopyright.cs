using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>copyright</c> object found on albums. The
    /// authoritative meaning of each field is the Spotify Web API object model; <c>[JsonPropertyName]</c>
    /// attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyCopyright {

        /// <summary>Copyright statement text. Maps to <c>text</c>.</summary>
        [JsonPropertyName( "text" )]
        public string Text { get; set; } = string.Empty;

        /// <summary>Copyright type: <c>C</c> for the copyright and <c>P</c> for the sound-recording (performance) copyright. Maps to <c>type</c>.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;
    }

}
