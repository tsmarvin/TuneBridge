using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the Apple Music API <c>Artwork</c> object. Deserialization target only; the
    /// authoritative field meanings are defined by the Apple Music API.
    /// </summary>
    /// <remarks>
    /// The <see cref="Url"/> is a template containing <c>{w}</c> and <c>{h}</c> placeholders that callers
    /// replace with the desired pixel width and height. For example:
    /// "https://is1-ssl.mzstatic.com/image/{w}x{h}bb.jpg".
    /// </remarks>
    public sealed class AppleMusicArtwork {

        /// <summary>The maximum available artwork width, in pixels.</summary>
        [JsonPropertyName( "width" )]
        public int Width { get; set; }

        /// <summary>The maximum available artwork height, in pixels.</summary>
        [JsonPropertyName( "height" )]
        public int Height { get; set; }

        /// <summary>The artwork URL template, with <c>{w}</c> and <c>{h}</c> placeholders for width and height.</summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

        /// <summary>The artwork background color as a hex string; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "bgColor" )]
        public string? BackgroundColor { get; set; }

        /// <summary>The first text color as a hex string; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "textColor1" )]
        public string? TextColor1 { get; set; }

        /// <summary>The second text color as a hex string; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "textColor2" )]
        public string? TextColor2 { get; set; }

        /// <summary>The third text color as a hex string; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "textColor3" )]
        public string? TextColor3 { get; set; }

        /// <summary>The fourth text color as a hex string; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "textColor4" )]
        public string? TextColor4 { get; set; }
    }

}
