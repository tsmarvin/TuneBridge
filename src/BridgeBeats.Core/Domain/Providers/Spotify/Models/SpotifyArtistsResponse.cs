using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Response from GET /artists?ids={ids} - batch artist lookup.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /artists?ids={comma-separated-ids}
    /// Maximum: 50 artist IDs per request
    /// Documentation: https://developer.spotify.com/documentation/web-api/reference/get-multiple-artists
    /// </remarks>
    public sealed class SpotifyArtistsResponse {

        /// <summary>
        /// A list of artist objects. Position corresponds to the position of the ID in the request.
        /// If an artist is not found, null is returned at that position.
        /// </summary>
        [JsonPropertyName( "artists" )]
        public List<SpotifyArtist?> Artists { get; set; } = [];
    }

}
