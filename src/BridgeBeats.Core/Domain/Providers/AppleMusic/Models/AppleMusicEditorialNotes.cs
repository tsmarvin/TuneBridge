using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// Editorial notes for Apple Music content.
    /// </summary>
    public sealed class AppleMusicEditorialNotes {

        /// <summary>
        /// The editorial notes in standard format.
        /// </summary>
        [JsonPropertyName( "standard" )]
        public string? Standard { get; set; }

        /// <summary>
        /// The editorial notes in short format.
        /// </summary>
        [JsonPropertyName( "short" )]
        public string? Short { get; set; }

    }

}
