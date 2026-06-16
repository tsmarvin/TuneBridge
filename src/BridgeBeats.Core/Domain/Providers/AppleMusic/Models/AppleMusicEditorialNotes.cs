using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the Apple Music API <c>EditorialNotes</c> object. Deserialization target only.
    /// </summary>
    public sealed class AppleMusicEditorialNotes {

        /// <summary>The full-length editorial note; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "standard" )]
        public string? Standard { get; set; }

        /// <summary>The abbreviated editorial note; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "short" )]
        public string? Short { get; set; }

    }

}
