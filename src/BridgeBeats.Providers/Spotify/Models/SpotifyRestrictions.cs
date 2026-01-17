using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.Spotify.Models {

    /// <summary>
    /// Restrictions on a track or album (e.g., market availability).
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyRestrictions {

        /// <summary>
        /// The reason for the restriction.
        /// Values: "market" (not available in user's market), "product" (not available with user's subscription),
        /// "explicit" (explicit content blocked by user settings).
        /// </summary>
        [JsonPropertyName( "reason" )]
        public string Reason { get; set; } = string.Empty;
    }

}
