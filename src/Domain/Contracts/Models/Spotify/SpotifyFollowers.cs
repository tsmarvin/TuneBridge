using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// Followers information for an artist or playlist.
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyFollowers {

        /// <summary>
        /// A link to the Web API endpoint providing full details of the followers.
        /// Currently always null as the API doesn't support this.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>
        /// The total number of followers.
        /// </summary>
        [JsonPropertyName( "total" )]
        public int Total { get; set; }
    }

}
