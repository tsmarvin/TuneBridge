using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// Copyright information for an album.
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyCopyright {

        /// <summary>
        /// The copyright text.
        /// </summary>
        [JsonPropertyName( "text" )]
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The type of copyright: C = copyright, P = performance copyright.
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;
    }

}
