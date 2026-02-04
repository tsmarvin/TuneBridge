using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// An image associated with a Spotify resource (album art, artist photo, etc.).
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyImage {

        /// <summary>
        /// The source URL of the image.
        /// </summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The image height in pixels.
        /// </summary>
        [JsonPropertyName( "height" )]
        public int? Height { get; set; }

        /// <summary>
        /// The image width in pixels.
        /// </summary>
        [JsonPropertyName( "width" )]
        public int? Width { get; set; }
    }

}
