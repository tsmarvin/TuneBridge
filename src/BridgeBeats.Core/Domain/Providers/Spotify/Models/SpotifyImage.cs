using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>image</c> object used for cover art and
    /// artist images. The authoritative meaning of each field is the Spotify Web API object model;
    /// <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    public sealed class SpotifyImage {

        /// <summary>Source URL of the image. Maps to <c>url</c>.</summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

        /// <summary>Image height in pixels; null when Spotify does not report it. Maps to <c>height</c>.</summary>
        [JsonPropertyName( "height" )]
        public int? Height { get; set; }

        /// <summary>Image width in pixels; null when Spotify does not report it. Maps to <c>width</c>.</summary>
        [JsonPropertyName( "width" )]
        public int? Width { get; set; }
    }

}
