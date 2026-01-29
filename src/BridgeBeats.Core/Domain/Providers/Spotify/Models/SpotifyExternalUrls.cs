using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// External URLs for a Spotify resource.
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyExternalUrls {

        /// <summary>
        /// The Spotify URL for the object (e.g., "https://open.spotify.com/track/...").
        /// </summary>
        [JsonPropertyName( "spotify" )]
        public string Spotify { get; set; } = string.Empty;
    }

}
