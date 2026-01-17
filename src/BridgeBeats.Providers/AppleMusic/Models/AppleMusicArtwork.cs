using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.AppleMusic.Models {

    /// <summary>
    /// Represents artwork (cover art) for Apple Music content (songs, albums).
    /// </summary>
    /// <remarks>
    /// The URL template uses {w} and {h} placeholders that must be replaced with desired dimensions.
    /// Example: "https://is1-ssl.mzstatic.com/image/{w}x{h}bb.jpg"
    /// </remarks>
    public sealed class AppleMusicArtwork {

        /// <summary>
        /// The maximum width available for the image in pixels.
        /// </summary>
        [JsonPropertyName( "width" )]
        public int Width { get; set; }

        /// <summary>
        /// The maximum height available for the image in pixels.
        /// </summary>
        [JsonPropertyName( "height" )]
        public int Height { get; set; }

        /// <summary>
        /// A URL template for the artwork image.
        /// Replace {w} and {h} with desired width and height values.
        /// </summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The background color of the artwork in hex format.
        /// </summary>
        [JsonPropertyName( "bgColor" )]
        public string? BackgroundColor { get; set; }

        /// <summary>
        /// The primary text color for the artwork in hex format.
        /// </summary>
        [JsonPropertyName( "textColor1" )]
        public string? TextColor1 { get; set; }

        /// <summary>
        /// The secondary text color for the artwork in hex format.
        /// </summary>
        [JsonPropertyName( "textColor2" )]
        public string? TextColor2 { get; set; }

        /// <summary>
        /// The tertiary text color for the artwork in hex format.
        /// </summary>
        [JsonPropertyName( "textColor3" )]
        public string? TextColor3 { get; set; }

        /// <summary>
        /// The quaternary text color for the artwork in hex format.
        /// </summary>
        [JsonPropertyName( "textColor4" )]
        public string? TextColor4 { get; set; }
    }

}
